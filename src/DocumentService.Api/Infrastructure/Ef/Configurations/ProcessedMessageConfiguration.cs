using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentService.Api.Infrastructure.Ef.Configurations
{
    public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
    {
        public void Configure(EntityTypeBuilder<ProcessedMessage> msg)
        {
            msg.ToTable("ProcessedMessages");

            // The composite key is the mechanism, not just a key: a concurrent second
            // delivery of the same event fails to insert, which is what makes the guard
            // race-safe rather than merely check-then-act.
            msg.HasKey(m => new { m.MessageId, m.Handler });

            msg.Property(m => m.Handler)
               .IsRequired()
               .HasMaxLength(200);

            msg.Property(m => m.ProcessedAtUtc)
               .IsRequired();
        }
    }
}
