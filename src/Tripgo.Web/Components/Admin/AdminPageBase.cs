using Microsoft.AspNetCore.Components;
using Tripgo.Application.DTOs.Admin;
using Tripgo.Application.Interfaces;
using Tripgo.Domain;

namespace Tripgo.Web.Components.Admin;

/// <summary>The feedback every admin action shares: busy state, success and error messages, and failure handling.</summary>
public abstract class AdminPageBase : ComponentBase
{
    [Inject] protected IAdminService Admin { get; set; } = default!;
    [Inject] protected ILogger<AdminPageBase> PageLogger { get; set; } = default!;

    protected bool IsBusy { get; private set; }
    protected string? StatusMessage { get; set; }
    protected bool StatusIsError { get; set; }

    protected async Task<bool> RunAsync(Func<Task<AdminResult>> action, Func<Task>? onSuccess = null)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var result = await action();
            StatusMessage = result.Message;
            StatusIsError = !result.Success;
            if (result.Success && onSuccess != null)
            {
                await onSuccess();
            }

            return result.Success;
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusMessage = exception.Message;
            StatusIsError = true;
            return false;
        }
        catch (Exception exception)
        {
            PageLogger.LogError(exception, "Admin action failed.");
            StatusMessage = "Không thể hoàn tất thao tác lúc này. Vui lòng thử lại.";
            StatusIsError = true;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task LoadAsync(Func<Task> load)
    {
        try
        {
            await load();
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusMessage = exception.Message;
            StatusIsError = true;
        }
        catch (Exception exception)
        {
            PageLogger.LogError(exception, "Admin page load failed.");
            StatusMessage = "Không thể tải dữ liệu lúc này. Vui lòng thử lại.";
            StatusIsError = true;
        }
    }

    protected static string Vn(DateTimeOffset value) => value.ToVietnam().ToString("dd/MM/yyyy HH:mm");
    protected static string Money(decimal value) => value.ToString("N0") + " đ";

    protected static string DataSourceLabel(string source) => source switch
    {
        "SandboxFixture" => "Sandbox",
        "ReferenceSchedule" => "Lịch tham khảo",
        "OperatorVerified" => "Nhà xe xác nhận",
        "AuthorizedFeed" => "Nguồn được ủy quyền",
        _ => "Chưa xác minh"
    };
}
