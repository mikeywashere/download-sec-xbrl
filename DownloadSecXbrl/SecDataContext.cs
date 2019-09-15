using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DownloadSecXbrl
{

    public class SecDataContext : DbContext
    {
        public DbSet<Cik> Ciks { get; set; }

        public DbSet<Filing> Filings { get; set; }

        public DbSet<SecFile> Files { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(
                @"Server=(localdb);Database=sec.xbrl;Integrated Security=True");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Filing>()
                .HasOne(p => p.Cik)
                .WithMany(p => p.Filings)
                .HasForeignKey(p => p.CikId);

            modelBuilder.Entity<SecFile>()
                .HasOne(p => p.Filing)
                .WithMany(p => p.Files)
                .HasForeignKey(p => p.FilingId);

        }
    }

    public class Cik    
    {
        [Column("cik_id")]
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CikId { get; set; }

        [Column("cik_number")]
        public int CikNumber { get; set; }

        [Column("cik_text")]
        public string CikText { get; set; }

        public List<Filing> Filings { get; set; }
    }

    public class Filing
    {
        [Column("filing_id")]
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FilingId { get; set; }

        [Column("cik_id")]
        public int CikId { get; set; }

        [Column("filing_date")]
        public DateTime FilingDate { get; set; }

        [Column("document_type")]
        public string DocumentType { get; set; }

        public List<SecFile> Files { get; set; }

        public Cik Cik { get; set; }
    }

    public class SecFile
    {
        [Column("file_id")]
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileId { get; set; }

        [Column("filing_id")]
        public int FilingId { get; set; }

        [Column("file_name")]
        public string FileName { get; set; }

        [Column("file_content")]
        public string FileContents { get; set; }

        public Filing Filing { get; set; }
    }
}
