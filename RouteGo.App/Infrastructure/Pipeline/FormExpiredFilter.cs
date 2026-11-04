using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// A submit whose anti-forgery token is missing or no longer valid changes nothing: instead of the
/// framework's bare 400, the person is sent back to the same page with the "form has expired"
/// message, so a fresh form (with a fresh token) is in front of them.
/// </summary>
public sealed class FormExpiredFilter : IAsyncAlwaysRunResultFilter
{
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public FormExpiredFilter(ITempDataDictionaryFactory tempDataFactory)
    {
        _tempDataFactory = tempDataFactory;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            ITempDataDictionary tempData = _tempDataFactory.GetTempData(context.HttpContext);
            tempData[Flash.Key] = Messages.ProblemFormExpired;
            tempData[Flash.KindKey] = Flash.KindDanger;
            // The request was stopped before the framework's own TempData save runs, so save here.
            tempData.Save();
            HttpRequest request = context.HttpContext.Request;
            context.Result = new RedirectResult(request.PathBase + request.Path + request.QueryString);
        }

        await next();
    }
}
