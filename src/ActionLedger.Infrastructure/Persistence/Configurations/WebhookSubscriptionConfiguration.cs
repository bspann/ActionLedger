using ActionLedger.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>webhook_subscriptions</c> table (AD-8). Snake_case names and <c>ValueGeneratedNever</c> on
/// the key come from <see cref="ModelConventions"/>; the provider maps <c>event_types</c> to
/// <c>text[]</c>, exactly as it maps a Meeting's attendees.
/// </summary>
/// <remarks>
/// No concurrency token: nothing updates a subscription yet, and AD-20's four token-carrying roots
/// do not include it.
/// </remarks>
internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.Url)
            .IsRequired()
            .HasMaxLength(WebhookSubscription.UrlMaxLength);

        builder.Property(subscription => subscription.Secret)
            .IsRequired()
            .HasMaxLength(WebhookSubscription.SecretMaxLength);

        builder.Property(subscription => subscription.IsActive)
            .IsRequired();

        // The provider maps this to text[]; only the nullability is this configuration's business.
        builder.Property(subscription => subscription.EventTypes)
            .IsRequired();

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(subscription => subscription.DomainEvents);
    }
}
