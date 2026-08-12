using BlazorLppp.Domain.Entities;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlazorLppp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();

    public DbSet<TestDocument> TestDocuments => Set<TestDocument>();

    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();

    public DbSet<TestOption> TestOptions => Set<TestOption>();

    public DbSet<TestAnswer> TestAnswers => Set<TestAnswer>();

    public DbSet<TestScaleResult> TestScaleResults => Set<TestScaleResult>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Number)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.HasIndex(e => e.Number).IsUnique();
            entity.HasIndex(e => e.Name);
        });

        builder.Entity<Employee>(entity =>
        {
            entity.ToTable("Employees");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MiddleName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.HasOne(e => e.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.DepartmentId, e.LastName, e.FirstName, e.MiddleName })
                .IsUnique();
        });

        builder.Entity<TestScaleResult>(entity =>
        {
            entity.ToTable("TestScaleResults");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ScaleCode)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(e => e.Interpretation)
                .HasMaxLength(1000);

            entity.HasOne(e => e.TestAttempt)
                .WithMany(a => a.ScaleResults)
                .HasForeignKey(e => e.TestAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.TestAttemptId, e.ScaleCode }).IsUnique();
        });

        builder.Entity<TestDocument>(entity =>
        {
            entity.ToTable("TestDocuments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(300);

            entity.Property(e => e.Instruction)
                .HasMaxLength(2000);

            entity.Property(e => e.OriginalFileName)
                .IsRequired()
                .HasMaxLength(260);

            entity.Property(e => e.FolderName)
                .IsRequired()
                .HasMaxLength(260);

            entity.Property(e => e.RelativePath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.UploadedAt)
                .IsRequired();

            entity.Property(e => e.IsRequired)
                .HasDefaultValue(false);

            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsRequired);
            entity.HasIndex(e => e.RelativePath).IsUnique();
        });

        builder.Entity<TestQuestion>(entity =>
        {
            entity.ToTable("TestQuestions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Text)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(e => e.Hint)
                .HasMaxLength(1000);

            entity.Property(e => e.Type)
                .IsRequired()
                .HasConversion<int>();

            entity.HasOne(e => e.TestDocument)
                .WithMany(d => d.Questions)
                .HasForeignKey(e => e.TestDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.TestDocumentId, e.SortOrder });
        });

        builder.Entity<TestOption>(entity =>
        {
            entity.ToTable("TestOptions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Key)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Text)
                .IsRequired()
                .HasMaxLength(1000);

            entity.HasOne(e => e.TestQuestion)
                .WithMany(q => q.Options)
                .HasForeignKey(e => e.TestQuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.TestQuestionId, e.SortOrder });
        });

        builder.Entity<TestAnswer>(entity =>
        {
            entity.ToTable("TestAnswers");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.TestAttempt)
                .WithMany(a => a.Answers)
                .HasForeignKey(e => e.TestAttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TestQuestion)
                .WithMany()
                .HasForeignKey(e => e.TestQuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SelectedOption)
                .WithMany()
                .HasForeignKey(e => e.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.TestAttemptId, e.TestQuestionId }).IsUnique();
        });

        builder.Entity<TestAttempt>(entity =>
        {
            entity.ToTable("TestAttempts");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.MiddleName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.StartedAt)
                .IsRequired()
                .HasDefaultValueSql("GETDATE()");

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(e => e.NumberUnit)
                .IsRequired();

            entity.Property(e => e.ResultRelativePath)
                .HasMaxLength(500);

            entity.Property(e => e.ResultFileName)
                .HasMaxLength(260);

            entity.HasOne(e => e.TestDocument)
                .WithMany()
                .HasForeignKey(e => e.TestDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Employee)
                .WithMany(emp => emp.Attempts)
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.EmployeeId);
        });
    }
}
