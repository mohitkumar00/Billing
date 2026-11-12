namespace BillMate.ClientCore.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route);
}
