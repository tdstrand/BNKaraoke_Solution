using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BNKaraoke.Api.Models;

namespace BNKaraoke.Api.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Song> Songs { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<QueueItem> QueueItems { get; set; }
        public DbSet<FavoriteSong> FavoriteSongs { get; set; }
        public DbSet<EventQueue> EventQueues { get; set; }
        public DbSet<EventAttendance> EventAttendances { get; set; }
        public DbSet<EventAttendanceHistory> EventAttendanceHistories { get; set; }
        public DbSet<RegistrationSettings> RegistrationSettings { get; set; }
        public DbSet<PinChangeHistory> PinChangeHistory { get; set; }
        public DbSet<KaraokeChannel> KaraokeChannels { get; set; }
        public DbSet<ApiSettings> ApiSettings { get; set; }
        public DbSet<SingerStatus> SingerStatus { get; set; } // Added

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ApplicationUser
            modelBuilder.Entity<ApplicationUser>()
                .Property(u => u.LastActivity).HasColumnName("LastActivity");

            // Event
            modelBuilder.Entity<Event>()
                .ToTable("Events", "public", t =>
                {
                    t.HasCheckConstraint("CK_Event_Status", "Status IN ('Upcoming', 'Live', 'Archived')");
                    t.HasCheckConstraint("CK_Event_Visibility", "Visibility IN ('Hidden', 'Visible')");
                })
                .HasKey(e => e.EventId);

            modelBuilder.Entity<Event>()
                .HasMany(e => e.EventQueues)
                .WithOne(eq => eq.Event)
                .HasForeignKey(eq => eq.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Event>()
                .HasMany(e => e.EventAttendances)
                .WithOne(ea => ea.Event)
                .HasForeignKey(ea => ea.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Event>()
                .Property(e => e.EventId).HasColumnName("EventId");
            modelBuilder.Entity<Event>()
                .Property(e => e.EventCode).HasColumnName("EventCode");
            modelBuilder.Entity<Event>()
                .Property(e => e.Description).HasColumnName("Description");
            modelBuilder.Entity<Event>()
                .Property(e => e.Status).HasColumnName("Status").HasMaxLength(20).HasDefaultValue("Upcoming");
            modelBuilder.Entity<Event>()
                .Property(e => e.Visibility).HasColumnName("Visibility").HasMaxLength(20).HasDefaultValue("Visible");
            modelBuilder.Entity<Event>()
                .Property(e => e.Location).HasColumnName("Location");
            modelBuilder.Entity<Event>()
                .Property(e => e.ScheduledDate).HasColumnName("ScheduledDate");
            modelBuilder.Entity<Event>()
                .Property(e => e.ScheduledStartTime).HasColumnName("ScheduledStartTime");
            modelBuilder.Entity<Event>()
                .Property(e => e.ScheduledEndTime).HasColumnName("ScheduledEndTime");
            modelBuilder.Entity<Event>()
                .Property(e => e.KaraokeDJName).HasColumnName("KaraokeDJName");
            modelBuilder.Entity<Event>()
                .Property(e => e.IsCanceled).HasColumnName("IsCanceled").HasDefaultValue(false);
            modelBuilder.Entity<Event>()
                .Property(e => e.RequestLimit).HasColumnName("RequestLimit").HasDefaultValue(15);
            modelBuilder.Entity<Event>()
                .Property(e => e.SongsCompleted).HasColumnName("SongsCompleted").HasDefaultValue(0);
            modelBuilder.Entity<Event>()
                .Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<Event>()
                .Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder.Entity<Event>()
                .HasIndex(e => e.EventCode)
                .IsUnique();

            // EventQueue
            modelBuilder.Entity<EventQueue>()
                .ToTable("EventQueues", "public", t =>
                {
                    t.HasCheckConstraint("CK_EventQueue_Status", "Status IN ('Upcoming', 'Live', 'Archived')");
                })
                .HasKey(eq => eq.QueueId);

            modelBuilder.Entity<EventQueue>()
                .HasOne(eq => eq.Event)
                .WithMany(e => e.EventQueues)
                .HasForeignKey(eq => eq.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventQueue>()
                .HasOne(eq => eq.Song)
                .WithMany()
                .HasForeignKey(eq => eq.SongId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventQueue>()
                .HasOne(eq => eq.Requestor)
                .WithMany()
                .HasForeignKey(eq => eq.RequestorUserName)
                .HasPrincipalKey(u => u.UserName)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.QueueId).HasColumnName("QueueId");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.EventId).HasColumnName("EventId");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.SongId).HasColumnName("SongId");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.RequestorUserName).HasColumnName("RequestorUserName").IsRequired().HasMaxLength(20);
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.Singers).HasColumnName("Singers").HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.Position).HasColumnName("Position");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.Status).HasColumnName("Status").IsRequired().HasMaxLength(20);
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.IsActive).HasColumnName("IsActive").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.WasSkipped).HasColumnName("WasSkipped").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.IsCurrentlyPlaying).HasColumnName("IsCurrentlyPlaying").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.SungAt).HasColumnName("SungAt");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.IsOnBreak).HasColumnName("IsOnBreak").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.CreatedAt).HasColumnName("CreatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<EventQueue>()
                .Property(eq => eq.UpdatedAt).HasColumnName("UpdatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");

            // EventAttendance
            modelBuilder.Entity<EventAttendance>()
                .ToTable("EventAttendance", "public")
                .HasKey(ea => ea.AttendanceId);

            modelBuilder.Entity<EventAttendance>()
                .HasOne(ea => ea.Event)
                .WithMany(e => e.EventAttendances)
                .HasForeignKey(ea => ea.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventAttendance>()
                .HasOne(ea => ea.Requestor)
                .WithMany()
                .HasForeignKey(ea => ea.RequestorId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventAttendance>()
                .HasMany(ea => ea.AttendanceHistories)
                .WithOne(eah => eah.Attendance)
                .HasForeignKey(eah => eah.AttendanceId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.AttendanceId).HasColumnName("AttendanceId");
            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.EventId).HasColumnName("EventId");
            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.RequestorId).HasColumnName("RequestorId");
            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.IsCheckedIn).HasColumnName("IsCheckedIn");
            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.IsOnBreak).HasColumnName("IsOnBreak");
            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.BreakStartAt).HasColumnName("BreakStartAt");
            modelBuilder.Entity<EventAttendance>()
                .Property(ea => ea.BreakEndAt).HasColumnName("BreakEndAt");

            modelBuilder.Entity<EventAttendance>()
                .HasIndex(ea => new { ea.EventId, ea.RequestorId })
                .IsUnique();

            // EventAttendanceHistory
            modelBuilder.Entity<EventAttendanceHistory>()
                .ToTable("EventAttendanceHistories", "public", t =>
                {
                    t.HasCheckConstraint("CK_EventAttendanceHistory_Action", "Action IN ('CheckIn', 'CheckOut')");
                })
                .HasKey(eah => eah.HistoryId);

            modelBuilder.Entity<EventAttendanceHistory>()
                .HasOne(eah => eah.Event)
                .WithMany()
                .HasForeignKey(eah => eah.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventAttendanceHistory>()
                .HasOne(eah => eah.Requestor)
                .WithMany()
                .HasForeignKey(eah => eah.RequestorId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventAttendanceHistory>()
                .HasOne(eah => eah.Attendance)
                .WithMany(ea => ea.AttendanceHistories)
                .HasForeignKey(eah => eah.AttendanceId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<EventAttendanceHistory>()
                .Property(eah => eah.HistoryId).HasColumnName("HistoryId");
            modelBuilder.Entity<EventAttendanceHistory>()
                .Property(eah => eah.EventId).HasColumnName("EventId");
            modelBuilder.Entity<EventAttendanceHistory>()
                .Property(eah => eah.RequestorId).HasColumnName("RequestorId");
            modelBuilder.Entity<EventAttendanceHistory>()
                .Property(eah => eah.Action).HasColumnName("Action").HasMaxLength(20);
            modelBuilder.Entity<EventAttendanceHistory>()
                .Property(eah => eah.ActionTimestamp).HasColumnName("ActionTimestamp").HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<EventAttendanceHistory>()
                .Property(eah => eah.AttendanceId).HasColumnName("AttendanceId");

            // Song
            modelBuilder.Entity<Song>()
                .ToTable("Songs", "public")
                .HasKey(s => s.Id);

            modelBuilder.Entity<Song>()
                .Property(s => s.Id).HasColumnName("Id");
            modelBuilder.Entity<Song>()
                .Property(s => s.Title).HasColumnName("Title");
            modelBuilder.Entity<Song>()
                .Property(s => s.Artist).HasColumnName("Artist");
            modelBuilder.Entity<Song>()
                .Property(s => s.Genre).HasColumnName("Genre");
            modelBuilder.Entity<Song>()
                .Property(s => s.YouTubeUrl).HasColumnName("YouTubeUrl");
            modelBuilder.Entity<Song>()
                .Property(s => s.Status).HasColumnName("Status");
            modelBuilder.Entity<Song>()
                .Property(s => s.ApprovedBy).HasColumnName("ApprovedBy");
            modelBuilder.Entity<Song>()
                .Property(s => s.Bpm).HasColumnName("Bpm").HasDefaultValue(0f);
            modelBuilder.Entity<Song>()
                .Property(s => s.Popularity).HasColumnName("Popularity").HasDefaultValue(0);
            modelBuilder.Entity<Song>()
                .Property(s => s.RequestDate).HasColumnName("RequestDate").HasDefaultValueSql("'-infinity'::timestamp with time zone");
            modelBuilder.Entity<Song>()
                .Property(s => s.RequestedBy).HasColumnName("RequestedBy").HasDefaultValue("");
            modelBuilder.Entity<Song>()
                .Property(s => s.SpotifyId).HasColumnName("SpotifyId").HasDefaultValue("");
            modelBuilder.Entity<Song>()
                .Property(s => s.Valence).HasColumnName("Valence").HasDefaultValue(0);
            modelBuilder.Entity<Song>()
                .Property(s => s.Decade).HasColumnName("Decade");
            modelBuilder.Entity<Song>()
                .Property(s => s.MusicBrainzId).HasColumnName("MusicBrainzId");
            modelBuilder.Entity<Song>()
                .Property(s => s.Mood).HasColumnName("Mood");
            modelBuilder.Entity<Song>()
                .Property(s => s.LastFmPlaycount).HasColumnName("LastFmPlaycount");
            modelBuilder.Entity<Song>()
                .Property(s => s.Danceability).HasColumnName("Danceability");
            modelBuilder.Entity<Song>()
                .Property(s => s.Energy).HasColumnName("Energy");

            // QueueItem
            modelBuilder.Entity<QueueItem>()
                .ToTable("QueueItems", "public")
                .HasKey(qi => qi.Id);

            modelBuilder.Entity<QueueItem>()
                .Property(qi => qi.Id).HasColumnName("Id");

            // FavoriteSong
            modelBuilder.Entity<FavoriteSong>()
                .ToTable("FavoriteSongs", "public")
                .HasKey(fs => fs.Id);

            modelBuilder.Entity<FavoriteSong>()
                .Property(fs => fs.Id).HasColumnName("Id");
            modelBuilder.Entity<FavoriteSong>()
                .Property(fs => fs.SingerId).HasColumnName("SingerId");
            modelBuilder.Entity<FavoriteSong>()
                .Property(fs => fs.SongId).HasColumnName("SongId");

            // RegistrationSettings
            modelBuilder.Entity<RegistrationSettings>()
                .ToTable("RegistrationSettings", "public")
                .HasKey(rs => rs.Id);

            // PinChangeHistory
            modelBuilder.Entity<PinChangeHistory>()
                .ToTable("PinChangeHistories", "public")
                .HasKey(pch => pch.Id);

            // KaraokeChannel
            modelBuilder.Entity<KaraokeChannel>()
                .ToTable("KaraokeChannels", "public")
                .HasKey(kc => kc.Id);

            modelBuilder.Entity<KaraokeChannel>()
                .Property(kc => kc.Id).HasColumnName("Id");
            modelBuilder.Entity<KaraokeChannel>()
                .Property(kc => kc.ChannelName).HasColumnName("ChannelName").IsRequired().HasMaxLength(100);
            modelBuilder.Entity<KaraokeChannel>()
                .Property(kc => kc.ChannelId).HasColumnName("ChannelId").HasMaxLength(100);
            modelBuilder.Entity<KaraokeChannel>()
                .Property(kc => kc.SortOrder).HasColumnName("SortOrder").IsRequired();
            modelBuilder.Entity<KaraokeChannel>()
                .Property(kc => kc.IsActive).HasColumnName("IsActive").IsRequired().HasDefaultValue(true);

            // ApiSettings
            modelBuilder.Entity<ApiSettings>()
                .ToTable("ApiSettings", "public")
                .HasKey(s => s.Id);

            modelBuilder.Entity<ApiSettings>()
                .Property(s => s.Id).HasColumnName("Id");
            modelBuilder.Entity<ApiSettings>()
                .Property(s => s.SettingKey).HasColumnName("SettingKey").IsRequired().HasMaxLength(50);
            modelBuilder.Entity<ApiSettings>()
                .Property(s => s.SettingValue).HasColumnName("SettingValue").IsRequired().HasMaxLength(100);
            modelBuilder.Entity<ApiSettings>()
                .Property(s => s.CreatedAt).HasColumnName("CreatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");
            modelBuilder.Entity<ApiSettings>()
                .Property(s => s.UpdatedAt).HasColumnName("UpdatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");

            // SingerStatus
            modelBuilder.Entity<SingerStatus>()
                .ToTable("SingerStatus", "public")
                .HasKey(ss => ss.Id);

            modelBuilder.Entity<SingerStatus>()
                .HasOne(ss => ss.Event)
                .WithMany()
                .HasForeignKey(ss => ss.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SingerStatus>()
                .HasOne(ss => ss.Requestor)
                .WithMany()
                .HasForeignKey(ss => ss.RequestorId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.Id).HasColumnName("Id");
            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.EventId).HasColumnName("EventId");
            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.RequestorId).HasColumnName("RequestorId");
            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.IsLoggedIn).HasColumnName("IsLoggedIn").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.IsJoined).HasColumnName("IsJoined").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.IsOnBreak).HasColumnName("IsOnBreak").IsRequired().HasDefaultValue(false);
            modelBuilder.Entity<SingerStatus>()
                .Property(ss => ss.UpdatedAt).HasColumnName("UpdatedAt").IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder.Entity<SingerStatus>()
                .HasIndex(ss => new { ss.EventId, ss.RequestorId })
                .IsUnique();
        }
    }
}