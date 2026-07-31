using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentService.Api.Infrastructure.Ef.Configurations
{
    public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> outbox)
        {
            outbox.ToTable("OutboxMessages");
            outbox.HasKey(m => m.Id);

            outbox.Property(m => m.Id).ValueGeneratedNever();

            outbox.Property(m => m.EntityName).IsRequired().HasMaxLength(260);
            outbox.Property(m => m.MessageType).IsRequired().HasMaxLength(260);
            outbox.Property(m => m.Payload).IsRequired();

            // "00-<32 hex trace id>-<16 hex span id>-<2 hex flags>" is 55 characters;
            // the cap leaves room without inviting anything else into the column.
            outbox.Property(m => m.TraceParent).HasMaxLength(128);
            outbox.Property(m => m.CreatedAtUtc).IsRequired();
            outbox.Property(m => m.LastError).HasMaxLength(2000);

            // The relay's only query is "oldest unsent first", so index exactly that.
            // Filtered, because sent rows are the overwhelming majority over time and
            // there is no reason to carry them in the index.
            outbox.HasIndex(m => new { m.SentAtUtc, m.CreatedAtUtc })
                  .HasFilter("[SentAtUtc] IS NULL")
                  .HasDatabaseName("IX_OutboxMessages_Pending");
        }
    }
}
