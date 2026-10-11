using DESD.LifeHub.Core.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DESD.LifeHub.Web.Pages.Account;

public class WorkspaceSelectorModel : PageModel
{
    public WorkspaceSelectorModel() : this(new List<Workspace>())
    {
    }

    public WorkspaceSelectorModel(IReadOnlyList<Workspace> workspaces)
    {
        Workspaces = workspaces;
    }

    public IReadOnlyList<Workspace> Workspaces { get; set; } = new List<Workspace>();
}