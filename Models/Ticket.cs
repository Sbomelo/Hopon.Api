using System.Security.Cryptography;

namespace Hopon.Api.Models;

public class Ticket
{
    public int Id { get; set;}
    public string TicketReference { get; set; } = string.Empty;

    public int UserId { get; set; } 
    public User User {get; set;} = null!;

    public int TripId {get; set; }
    public Trip Trip { get; set; } = null!;

    public string? SetNumber {get; set; } 
    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    public ICollection<BoardingLog> BoardingLogs { get; set; } = new List<BoardingLog>();
}