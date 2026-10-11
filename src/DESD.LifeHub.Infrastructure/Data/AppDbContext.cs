using DESD.LifeHub.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DESD.LifeHub.Infrastructure.Data;

/// <summary>
/// Zentraler EF-Core-Kontext. Enthält Identity-Tabellen, Fachdaten und Mandantenstruktur.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<UserLicense> UserLicenses => Set<UserLicense>();
    public DbSet<CompanyLicense> CompanyLicenses => Set<CompanyLicense>();
    public DbSet<LicenseValidationLog> LicenseValidationLogs => Set<LicenseValidationLog>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<CalendarEventParticipant> CalendarEventParticipants => Set<CalendarEventParticipant>();
    public DbSet<CalendarShare> CalendarShares => Set<CalendarShare>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<CompanyAnnouncement> CompanyAnnouncements => Set<CompanyAnnouncement>();
    public DbSet<AbsenceRequest> AbsenceRequests => Set<AbsenceRequest>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.PreferredLanguage).HasMaxLength(10).IsRequired();
            e.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired();
            e.Property(x => x.ThemePreference).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.IsActive);
            e.HasOne(x => x.PersonalOrganization)
                .WithMany()
                .HasForeignKey(x => x.PersonalOrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<IdentityRole<Guid>>(e => e.ToTable("roles"));
        builder.Entity<IdentityUserRole<Guid>>(e => e.ToTable("user_roles"));
        builder.Entity<IdentityUserClaim<Guid>>(e => e.ToTable("user_claims"));
        builder.Entity<IdentityUserLogin<Guid>>(e => e.ToTable("user_logins"));
        builder.Entity<IdentityUserToken<Guid>>(e => e.ToTable("user_tokens"));
        builder.Entity<IdentityRoleClaim<Guid>>(e => e.ToTable("role_claims"));

        builder.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrganizationMember>(e =>
        {
            e.ToTable("organization_members");
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserLicense>(e =>
        {
            e.ToTable("user_licenses");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.LicenseKeyCiphertext).HasMaxLength(2048).IsRequired();
            e.Property(x => x.LicenseKeyHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.LicenseKeyPrefix).HasMaxLength(40).IsRequired();
            e.Property(x => x.LastReason).HasMaxLength(60);
            e.Property(x => x.ProductCode).HasMaxLength(80);
            e.Property(x => x.PolicyName).HasMaxLength(160);
            e.Property(x => x.LastError).HasMaxLength(400);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();

            // Ein Benutzer darf genau eine Lizenz besitzen.
            e.HasIndex(x => x.UserId).IsUnique();

            // Ein Schlüssel darf niemals zwei Konten zugewiesen werden. Serverseitige
            // Absicherung gegen parallele Aktivierungen.
            e.HasIndex(x => x.LicenseKeyHash).IsUnique();

            e.HasIndex(x => x.Status);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<LicenseValidationLog>(e =>
        {
            e.ToTable("license_validation_logs");
            e.Property(x => x.Outcome).HasMaxLength(30).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(60);
            e.Property(x => x.Diagnostic).HasMaxLength(400);
            e.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        builder.Entity<CompanyLicense>(e =>
        {
            e.ToTable("company_licenses");
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.Property(x => x.OpenLicensdLicenseId).HasMaxLength(80);
            e.Property(x => x.LicenseKeyCiphertext).HasMaxLength(2048).IsRequired();
            e.Property(x => x.LicenseKeyHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.LicenseKeyPrefix).HasMaxLength(40).IsRequired();
            e.Property(x => x.ProductCode).HasMaxLength(80);
            e.Property(x => x.PolicyName).HasMaxLength(160);

            // Ein Schlüssel darf nur einmal im Pool existieren (auch firmenübergreifend).
            e.HasIndex(x => x.LicenseKeyHash).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.AssignedToUserId });
            e.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedToUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000);
            e.Property(x => x.Category).HasMaxLength(80);
            e.Property(x => x.Tags).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.OwnerUserId, x.Status });
            e.HasIndex(x => new { x.OrganizationId, x.Status });
            e.HasIndex(x => x.DueAt);
            e.HasIndex(x => x.AssignedToUserId);
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Organization)
                .WithMany(o => o.Tasks)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedTo)
                .WithMany()
                .HasForeignKey(x => x.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CalendarEvent>(e =>
        {
            e.ToTable("calendar_events");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.DisplayTimeZoneId).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.OwnerUserId, x.StartsAt });
            e.HasIndex(x => new { x.OrganizationId, x.StartsAt });
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Organization)
                .WithMany(o => o.Events)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TaskItem)
                .WithMany()
                .HasForeignKey(x => x.TaskItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CalendarEvent>(e =>
        {
            e.Property(x => x.ColorCategory).HasMaxLength(30);
            e.Property(x => x.RecurrenceFrequency).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.RecurrenceFrequency);
            e.HasOne(x => x.Room)
                .WithMany()
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CalendarEventParticipant>(e =>
        {
            e.ToTable("calendar_event_participants");
            e.Property(x => x.Response).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.CalendarEventId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.CalendarEvent)
                .WithMany(c => c.Participants)
                .HasForeignKey(x => x.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CalendarShare>(e =>
        {
            e.ToTable("calendar_shares");
            e.HasIndex(x => new { x.OwnerUserId, x.SharedWithUserId }).IsUnique();
            e.HasIndex(x => x.SharedWithUserId);
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SharedWith)
                .WithMany()
                .HasForeignKey(x => x.SharedWithUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Room>(e =>
        {
            e.ToTable("rooms");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.ColorCategory).HasMaxLength(30);
            e.HasIndex(x => new { x.OrganizationId, x.IsActive });
            e.HasOne(x => x.Organization)
                .WithMany(o => o.Rooms)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskAttachment>(e =>
        {
            e.ToTable("task_attachments");
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(160).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasIndex(x => x.TaskItemId);
            e.HasOne(x => x.TaskItem)
                .WithMany(t => t.Attachments)
                .HasForeignKey(x => x.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UploadedBy)
                .WithMany()
                .HasForeignKey(x => x.UploadedByUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskComment>(e =>
        {
            e.ToTable("task_comments");
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.TaskItemId, x.CreatedAt });
            e.HasOne(x => x.TaskItem)
                .WithMany(t => t.Comments)
                .HasForeignKey(x => x.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CompanyAnnouncement>(e =>
        {
            e.ToTable("company_announcements");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.OrganizationId, x.PublishAt });
            e.HasOne(x => x.Organization)
                .WithMany(o => o.Announcements)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author)
                .WithMany()
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AbsenceRequest>(e =>
        {
            e.ToTable("absence_requests");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.OrganizationId, x.StartDate, x.EndDate });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Organization)
                .WithMany(o => o.AbsenceRequests)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedBy)
                .WithMany()
                .HasForeignKey(x => x.DecidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Note>(e =>
        {
            e.ToTable("notes");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Content).HasMaxLength(20000);
            e.Property(x => x.Category).HasMaxLength(80);
            e.Property(x => x.Tags).HasMaxLength(500);
            e.HasIndex(x => new { x.OwnerUserId, x.IsArchived });
            e.HasIndex(x => new { x.OrganizationId, x.IsSharedWithOrganization });
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Organization)
                .WithMany(o => o.Notes)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TaskItem)
                .WithMany(t => t.Notes)
                .HasForeignKey(x => x.TaskItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Reminder>(e =>
        {
            e.ToTable("reminders");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FailureReason).HasMaxLength(400);
            e.HasIndex(x => new { x.Status, x.RemindAt });
            e.HasIndex(x => new { x.UserId, x.RemindAt });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TaskItem)
                .WithMany(t => t.Reminders)
                .HasForeignKey(x => x.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CalendarEvent)
                .WithMany(c => c.Reminders)
                .HasForeignKey(x => x.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.Url).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reminder)
                .WithMany()
                .HasForeignKey(x => x.ReminderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.Property(x => x.Action).HasMaxLength(120).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(120);
            e.Property(x => x.EntityId).HasMaxLength(120);
            e.Property(x => x.Details).HasMaxLength(2000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Invitation>(e =>
        {
            e.ToTable("invitations");
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.Email);
            e.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
