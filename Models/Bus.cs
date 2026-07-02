using System.Security.Cryptography;

namespace Hopon.Api.Models;
public class Bus
{
    public int Id {get; set;}
    public string RegistrationNumber {get; set;} = string.Empty;
    public string? Model {get; set;}
    public int? Capacity { get; set;}
    public DateTime CreatedAt {get; set; } = DateTime.UtcNow;

    public ICollection<Trip> Trips {get; set;} = new List<Trip>();
}