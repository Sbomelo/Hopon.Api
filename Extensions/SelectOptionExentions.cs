using Hopon.Api.DTOs.Admin;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hopon.Api.Extensions;

public static class SelectOptionExtensions
{
    public static List<SelectListItem> ToSelectListItems(this IEnumerable<SelectOptionDto> options)
    {
        return options.Select(o => new SelectListItem(o.Label, o.Id.ToString())).ToList();
    }
}