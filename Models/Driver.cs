using System.Security.Cryptography;

namespace Hopon.Api.Models;

public class Driver
{
    public int Id {get; set;}
    public string FullName {get; set;} = string.Empty;
    public string PhoneNumber {get; set;} = string.Empty;
    public bool IsActive {get; set;} = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Trip> Trips {get; set;} = new List<Trip>();
}