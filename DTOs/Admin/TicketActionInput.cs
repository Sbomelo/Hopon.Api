using System.ComponentModel.DataAnnotations;

namespace Hopon.Api.DTOs.Admin;

public class TicketActionInput
{
    [Range(1, int.MaxValue)] public int TicketId { get; set; }
}

public enum AdminTripAction { Delay, Resume, Complete, Cancel }
