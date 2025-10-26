using DocumentService.Api.Infrastructure.Ef.Entities;
using Microsoft.EntityFrameworkCore;
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
            //Id-Spalte ist Primary Key deswegen hat automatisch schon einen Index in der Datenbank. Du brauchst also keinen zusätzlichen HasIndex(d => d.Id)

            // Columns
            doc.Property(d => d.Id)
               .ValueGeneratedNever(); // wir nehmen an: Guid kommt von außen, nicht DB
               //Da die DocumentId in meinem System über mehrere Services hinweg konsistent bleiben muss (z. B. API → Worker → Messages),
                //lasse ich die ID nicht von EF oder der Datenbank generieren, sondern erzeuge sie selbst.

            doc.Property(d => d.FileName)
               .IsRequired()
               .HasMaxLength(260); // Windows PFN Limit-ish / anpassen je nach Bedarf

            doc.Property(d => d.Status)
               .IsRequired()
               .HasConversion<string>()      // speichert Enum als string
               .HasMaxLength(50);            // schützt vor unendlich langen Enums

            doc.Property(d => d.AnalysisSummary)
               .HasMaxLength(4000); // schützt DB vor Romanen. Zahl kannst du anpassen.

            doc.Property(d => d.ExtractedEntities)
                .HasConversion(
                        // to provider (string in DB)
                        v => v == null ? null : string.Join("|||", v),

                        // from provider (back to string[])
                        v => v == null ? null : v.Split(new[] { "|||" }, StringSplitOptions.None)
                    )
                .HasColumnName("ExtractedEntities")
                .HasMaxLength(4000);

            doc.Property(d => d.AnalysisBlobRef)
                .HasMaxLength(500); // URL / blob path reference
                
            // Optimistic Concurrency / RowVersion
            doc.Property(d => d.RowVersion)
               .IsRowVersion()
               .IsConcurrencyToken(); // explizit machen ist nice

            // Optional: Indizes
            // Falls du häufig nach Status filterst (z.B. "alle PENDING Dokumente abholen")
            doc.HasIndex(d => d.Status);

            // Falls du oft nach FileName suchst (z.B. Duplikat-Prüfung)
            // doc.HasIndex(d => d.FileName);
        }
    }
}
