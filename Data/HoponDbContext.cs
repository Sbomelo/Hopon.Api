using Hopon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Data;

public class HoponDbContext : DbContext
{
    public HoponDbContext(DbContextOptions<HoponDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<EmergencyContact> EmergencyContacts { get; set; } = null!;
    public DbSet<Driver> Drivers { get; set; } = null!;
    public DbSet<Bus> Buses { get; set; } = null!;
    public DbSet<Stop> Stops { get; set; } = null!;
    public DbSet<BusRoute> BusRoutes { get; set; } = null!;
    public DbSet<RouteStop> RouteStops { get; set; } = null!;
    public DbSet<Trip> Trips { get; set; } = null!;
    public DbSet<TripStop> TripStops { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<LocationUpdate> LocationUpdates { get; set; } = null!;
    public DbSet<BoardingLog> BoardingLogs { get; set; } = null!;
    public DbSet<NotificationLog> NotificationLogs { get; set; } = null!;
    public DbSet<OtpRequest> OtpRequests { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //User
        modelBuilder.Entity<User>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique();
        

        //Emergency Contact
        modelBuilder.Entity<EmergencyContact>()
                .HasIndex(ec =>  ec.UserId)
                .IsUnique();

        modelBuilder.Entity<EmergencyContact>()
                .HasOne(ec => ec.User)
                .WithOne(u => u.EmergencyContact)
                .HasForeignKey<EmergencyContact>(ec => ec.UserId)
                .OnDelete(DeleteBehavior.Cascade);


        //Route Stop
        modelBuilder.Entity<RouteStop>()
                .HasOne( rs => rs.BusRoute)
                .WithMany( r => r.RouteStops)
                .HasForeignKey( rs => rs.BusRouteId)
                .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RouteStop>()
                .HasOne(rs => rs.Stop)
                .WithMany(s => s.RouteStops)
                .HasForeignKey(rs => rs.StopId)
                .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RouteStop>()
                .HasIndex(rs => new {rs.BusRouteId, rs.SequenceOrder})
                .IsUnique();


        //Trip
        modelBuilder.Entity<Trip>()
                .HasOne(t => t.BusRoute)
                .WithMany(r => r.Trips)
                .HasForeignKey(t => t.BusRouteId)
                .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<Trip>()
                .HasOne(t => t.Bus)
                .WithMany(b => b.Trips)
                .HasForeignKey(t => t.BusId)
                .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Trip>()
                .HasOne( t => t.Driver)
                .WithMany(d => d.Trips)
                .HasForeignKey(t => t.DriverId)
                .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Trip>()
                .HasIndex(t => t.Status);
                

        //TripStop
        modelBuilder.Entity<TripStop>()
                .HasOne(ts => ts.Trip)
                .WithMany(t => t.TripStops)
                .HasForeignKey( ts => ts.TripId)
                .OnDelete(DeleteBehavior.Cascade);
                
        modelBuilder.Entity<TripStop>()
                .HasOne(ts => ts.Stop)
                .WithMany(s => s.TripStops)
                .HasForeignKey(ts => ts.StopId)
                .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<TripStop>()
                .HasIndex(ts => new { ts.TripId, ts.SequenceOrder})
                .IsUnique();


        //Ticket
        modelBuilder.Entity<Ticket>()
                .HasIndex(tk => tk.TicketReference)
                .IsUnique();

        modelBuilder.Entity<Ticket>()
                .HasOne( tk => tk.User)
                .WithMany(u => u.Tickets)
                .HasForeignKey( tk => tk.UserId)
                .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Ticket>()
                .HasOne(tk => tk.Trip)
                .WithMany(t => t.Tickets)
                .HasForeignKey(tk => tk.TripId)
                .OnDelete(DeleteBehavior.Restrict);

        //A User can only hold one ticket per trip
        modelBuilder.Entity<Ticket>()
                .HasIndex(tk => new {tk.UserId, tk.TripId})
                .IsUnique();

        

        //Location Update
        modelBuilder.Entity<LocationUpdate>()
                .HasOne(lu => lu.Trip)
                .WithMany(t => t.LocationUpdates)
                .HasForeignKey( lu => lu.TripId)
                .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LocationUpdate>()
                .HasIndex( lu => new { lu.TripId, lu.RecordedAt});


        //Boarding Log
        modelBuilder.Entity<BoardingLog>()
                .HasOne(bl => bl.Ticket)
                .WithMany(tk => tk.BoardingLogs)
                .HasForeignKey(bl => bl.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BoardingLog>()
                .HasOne(bl => bl.Stop)
                .WithMany()
                .HasForeignKey(bl => bl.StopId)
                .OnDelete(DeleteBehavior.Restrict);

        
        // --- NotificationLog ---
        modelBuilder.Entity<NotificationLog>()
                .HasOne(nl => nl.User)
                .WithMany(u => u.NotificationLogs)
                .HasForeignKey(nl => nl.UserId)
                .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NotificationLog>()
                .HasOne(nl => nl.Trip)
                .WithMany()
                .HasForeignKey(nl => nl.TripId)
                .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<NotificationLog>()
                .HasIndex(nl => new { nl.UserId, nl.SentAt });

        
        //OtpRequest
        modelBuilder.Entity<OtpRequest>(entity =>
        {
                entity.HasIndex(o => new {o.PhoneNumber, o.CreatedAt});
                entity.Property(o => o.PhoneNumber).HasMaxLength(20).IsRequired();
                entity.Property( o => o.OtpCodeHash).HasMaxLength(255).IsRequired();
        });

    }
}