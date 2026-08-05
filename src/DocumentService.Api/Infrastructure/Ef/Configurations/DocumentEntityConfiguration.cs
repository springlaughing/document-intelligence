using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace DocumentService.Api.Infrastructure.Ef.Configurations
{
    public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
    {
        public void Configure(EntityTypeBuilder<DocumentEntity> doc)
        {
            // Tabelle + Key
            doc.ToTable("Documents"); // explizit, damit Migrations stabil bleiben
            doc.HasKey(d => d.Id);
            //Id-Spalte ist Primary Key deswegen hat automatisch schon einen Index in der Datenbank. Man keinen zusätzlichen HasIndex(d => d.Id)

            // Columns
            doc.Property(d => d.Id)
               .ValueGeneratedNever(); // wir nehmen an: Guid kommt von außen, nicht DB
               //Da die DocumentId in meinem System über mehrere Services hinweg konsistent bleiben muss (z. B. API → Worker → Messages),
                //lasse ich die ID nicht von EF oder der Datenbank generieren, sondern erzeuge sie selbst.

            doc.Property(d => d.FileName)
               .IsRequired()
               .HasMaxLength(260); 

            doc.Property(d => d.Status)
               .IsRequired()
               .HasConversion<string>()      // speichert Enum als string
               .HasMaxLength(50);            // schützt vor unendlich langen Enums

            doc.Property(d => d.AnalysisSummary)
               .HasMaxLength(4000); 

            doc.Property(d => d.ExtractedEntities)
                .HasConversion(
                        // to provider (string in DB)
                        v => v == null ? null : string.Join("|||", v),

                        // from provider (back to string[])
                        v => v == null ? null : v.Split(new[] { "|||" }, StringSplitOptions.None),

                        // A converted collection also needs a comparer. Without one EF
                        // compares the array by reference, so mutating it in place looks
                        // like no change at all and never gets saved. The snapshot must
                        // also be a copy, or the "original" value tracks the mutation.
                        new ValueComparer<string[]?>(
                            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
                            v => v == null
                                ? 0
                                : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                            v => v == null ? null : v.ToArray())
                    )
                .HasColumnName("ExtractedEntities")
                .HasMaxLength(4000);

            doc.Property(d => d.AnalysisBlobRef)
                .HasMaxLength(500); // URL / blob path reference

            doc.Property(d => d.FailureReason)
                .HasMaxLength(500);

            // Optimistic Concurrency / RowVersion
            doc.Property(d => d.RowVersion)
               .IsRowVersion()
               .IsConcurrencyToken(); 

            // Falls man häufig nach Status filtert (z.B. "alle PENDING Dokumente abholen").
            //
            // Composite rather than Status alone, because the reconciliation sweep runs
            // "Status == Analyzing AND AnalysisStartedAtUtc < cutoff" on every pass. Status
            // leads, so this still serves every lookup the single-column index served -
            // which is why that one is gone rather than kept alongside.
            doc.HasIndex(d => new { d.Status, d.AnalysisStartedAtUtc });

        }
    }
}
