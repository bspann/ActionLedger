#!/usr/bin/env bash
#
# Seed the ActionLedger Project board: one issue per story in epics.md, each added to the
# board. Idempotent — a second run creates nothing and reports what it found.
#
#   tools/seed-project-board.sh [--dry-run] [--no-board]
#
# --no-board creates the issues and labels only, skipping the Projects v2 board. Useful when the
# token carries `repo` but not `project`; re-run without the flag later to attach the board.
#
# Requires the gh CLI, authenticated with the `repo` and `project` scopes:
#
#   gh auth login
#   gh auth refresh --scopes project,read:project
#
# Environment overrides:
#   REPO           owner/name of the repository          (default bspann/ActionLedger)
#   PROJECT_TITLE  title of the Project board            (default ActionLedger v1.0.0)
#   EPICS_FILE     path to the epic breakdown            (default the planning artifact)

set -euo pipefail

REPO="${REPO:-bspann/ActionLedger}"
PROJECT_TITLE="${PROJECT_TITLE:-ActionLedger v1.0.0}"
OWNER="${REPO%%/*}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EPICS_FILE="${EPICS_FILE:-$repo_root/_bmad-output/planning-artifacts/epics.md}"

DRY_RUN=false
NO_BOARD=false
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=true ;;
    --no-board) NO_BOARD=true ;;
    *) echo "usage: $(basename "$0") [--dry-run] [--no-board]" >&2; exit 2 ;;
  esac
done

say()  { printf '%s\n' "$*"; }
step() { printf '\n== %s\n' "$*"; }
run()  { if $DRY_RUN; then printf '   would run: %s\n' "$*"; else "$@"; fi; }

# ---------------------------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------------------------

command -v gh >/dev/null 2>&1 || { echo "gh is not installed. See https://cli.github.com" >&2; exit 1; }
[ -f "$EPICS_FILE" ] || { echo "No epic breakdown at $EPICS_FILE" >&2; exit 1; }

gh auth status >/dev/null 2>&1 || { echo "gh is not authenticated. Run: gh auth login" >&2; exit 1; }

# Issues need only `repo`; the Projects v2 board needs `project`. --no-board skips that half.
if ! $NO_BOARD && ! gh project list --owner "$OWNER" --limit 1 >/dev/null 2>&1; then
  echo "The gh token cannot reach Projects v2. Run: gh auth refresh --scopes project,read:project" >&2
  echo "Or run with --no-board to create the issues only." >&2
  exit 1
fi

# ---------------------------------------------------------------------------------------------
# Parse the stories out of epics.md
#
# Emits one TSV row per story: epic number, epic title, story title, requirements line.
# ---------------------------------------------------------------------------------------------

stories="$(awk -F'\t' '
  /^## Epic [0-9]+:/ {
    line = substr($0, 9)                       # drop "## Epic "
    colon = index(line, ":")
    epic_number = substr(line, 1, colon - 1)
    epic_title = substr(line, colon + 2)
    sub(/ *\([^)]*\) *$/, "", epic_title)      # drop the trailing "(Saturday)" day hint
    next
  }
  /^### Story [0-9]+\.[0-9]+:/ {
    if (story_title != "") { print story_epic_number "\t" story_epic_title "\t" story_title "\t" requirements }
    story_title = substr($0, 5)                # drop "### ", keep "Story 1.1: ..."
    # Bind the epic now: a story is flushed when the *next* heading arrives, by which time
    # the epic may already have moved on.
    story_epic_number = epic_number
    story_epic_title = epic_title
    requirements = ""
    next
  }
  /^\*\*Requirements:\*\*/ {
    if (story_title != "" && requirements == "") { requirements = $0 }
    next
  }
  END {
    if (story_title != "") { print story_epic_number "\t" story_epic_title "\t" story_title "\t" requirements }
  }
' "$EPICS_FILE")"

story_count="$(printf '%s\n' "$stories" | grep -c . || true)"
[ "$story_count" -gt 0 ] || { echo "Parsed no stories out of $EPICS_FILE" >&2; exit 1; }

step "Seeding $REPO from $(basename "$EPICS_FILE") — $story_count stories"
$DRY_RUN && say "   (dry run: nothing is created)"

# ---------------------------------------------------------------------------------------------
# Labels — one per epic, plus `story`. --force makes this idempotent.
# ---------------------------------------------------------------------------------------------

step "Labels"

run gh label create story --repo "$REPO" --color 0E8A16 --description "A story from the epic breakdown" --force

printf '%s\n' "$stories" | cut -f1 | sort -u | while read -r epic_number; do
  [ -n "$epic_number" ] || continue
  run gh label create "epic-$epic_number" --repo "$REPO" --color 1D76DB --description "Epic $epic_number" --force
done

# ---------------------------------------------------------------------------------------------
# The Project board
# ---------------------------------------------------------------------------------------------

step "Project board"

if $NO_BOARD; then
  say "   skipped (--no-board); issues are created without board items"
  project_number=""
else
project_number="$(
  gh project list --owner "$OWNER" --limit 100 --format json \
    --jq '.projects[] | [.number, .title] | @tsv' 2>/dev/null |
  awk -F'\t' -v title="$PROJECT_TITLE" '$2 == title { print $1; exit }'
)"

if [ -n "$project_number" ]; then
  say "   reusing project #$project_number \"$PROJECT_TITLE\""
elif $DRY_RUN; then
  say "   would create project \"$PROJECT_TITLE\" owned by $OWNER"
  project_number=""
else
  project_number="$(
    gh project create --owner "$OWNER" --title "$PROJECT_TITLE" --format json --jq '.number'
  )"
  say "   created project #$project_number \"$PROJECT_TITLE\""
fi
fi

# ---------------------------------------------------------------------------------------------
# One issue per story
# ---------------------------------------------------------------------------------------------

step "Issues"

existing_issues="$(
  gh issue list --repo "$REPO" --state all --limit 500 --json number,title \
    --jq '.[] | [.number, .title] | @tsv'
)"

created=0
reused=0

while IFS=$'\t' read -r epic_number epic_title story_title requirements; do
  [ -n "$story_title" ] || continue

  issue_number="$(
    printf '%s\n' "$existing_issues" |
    awk -F'\t' -v title="$story_title" '$2 == title { print $1; exit }'
  )"

  if [ -n "$issue_number" ]; then
    say "   reusing #$issue_number  $story_title"
    reused=$((reused + 1))
  elif $DRY_RUN; then
    say "   would create     $story_title"
    created=$((created + 1))
    continue
  else
    body="$(cat <<BODY
**Epic $epic_number — $epic_title**

$requirements

Acceptance criteria live in the epic breakdown, under the heading "$story_title":
\`_bmad-output/planning-artifacts/epics.md\`

<sub>Seeded by \`tools/seed-project-board.sh\`.</sub>
BODY
)"

    issue_url="$(
      gh issue create --repo "$REPO" \
        --title "$story_title" \
        --body "$body" \
        --label story \
        --label "epic-$epic_number"
    )"
    issue_number="${issue_url##*/}"
    say "   created #$issue_number  $story_title"
    created=$((created + 1))
  fi

  # Adding an issue already on the board returns the existing item, so this is safe to repeat.
  if [ -n "$project_number" ]; then
    run gh project item-add "$project_number" --owner "$OWNER" \
      --url "https://github.com/$REPO/issues/$issue_number" >/dev/null
  fi
done <<< "$stories"

step "Done — $created created, $reused reused, $story_count stories in $(basename "$EPICS_FILE")"
