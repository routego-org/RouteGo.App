namespace RouteGo.App.Features.Operator.BusPhotos;

public sealed class BusPhotosModel : PageModel
{
    /// <summary>The most photos Fleet keeps for one bus.</summary>
    public const int MaxPhotos = 5;

    /// <summary>The largest file Fleet accepts, in bytes (3 MB).</summary>
    public const long MaxFileBytes = 3145728;

    public const string BusListAddress = "/operator/buses";
    public const string BackToListText = "Back to my buses";

    private const string PhotosPage = "/Operator/BusPhotos/BusPhotos";
    private const string PhotoSection = "_Photos";
    private const string Entity = "bus";
    private const string EntityLabel = "Photo";
    private const string ValidationFailed = "VALIDATION_FAILED";
    private const string PhotosChanged = "The photos changed since you opened this page. We have loaded them again.";
    private const string OrderSaved = "Photo order saved.";

    // The script asks once more after a file has not arrived; the second "not arrived" is final.
    private const int LastConfirmAttempt = 2;

    private readonly BusPhotosHandler _handler;

    public BusPhotosModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new BusPhotosHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public Guid BusId { get; set; }

    /// <summary>Set by a photo's "Delete" link: the question is asked before anything is sent.</summary>
    [BindProperty(SupportsGet = true, Name = "delete")]
    public Guid? DeleteQuestion { get; set; }

    /// <summary>The loaded bus; null when it could not be loaded or may not be shown.</summary>
    public PhotosBus? Bus { get; private set; }

    /// <summary>The photo the delete question is about; null when no question is shown.</summary>
    public Guid? ConfirmingPhotoId { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await ShowBusAsync(cancellationToken);
        AskAbout(DeleteQuestion);
    }

    /// <summary>The photo section alone, drawn again by the page script once every file is done.</summary>
    public async Task<IActionResult> OnGetSectionAsync(CancellationToken cancellationToken)
    {
        await ShowBusAsync(cancellationToken);
        return Partial(PhotoSection, this);
    }

    public async Task<IActionResult> OnPostOrderAsync(Guid photoId, string move, List<Guid> shownOrder, CancellationToken cancellationToken)
    {
        var request = new PhotoMoveRequest(shownOrder, photoId, move);
        ApiResult<PhotoList> api = await _handler.MovePhotoAsync(BusId, request, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = OrderSaved;
            return RedirectToPage(PhotosPage, new { busId = BusId });
        }

        if (api.Errors != null || api.Problem!.Code == ValidationFailed)
        {
            return await ShowAgainAsync(PageAlert.Warning(PhotosChanged), cancellationToken);
        }

        return await ShowRefusalAsync(api.Problem!, null, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid photoId, CancellationToken cancellationToken)
    {
        ApiResult<bool> api = await _handler.DeletePhotoAsync(BusId, photoId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessDeleted(EntityLabel);
            return RedirectToPage(PhotosPage, new { busId = BusId });
        }

        if (api.Problem!.Code == FleetDisplay.NotFound)
        {
            return await ShowAgainAsync(PageAlert.Warning(PhotosChanged), cancellationToken);
        }

        // When Fleet was down or failed, the question stays open so the operator can simply retry.
        return await ShowRefusalAsync(api.Problem!, photoId, cancellationToken);
    }

    /// <summary>The script's first step for one file: an upload slot from Fleet.</summary>
    public async Task<IActionResult> OnPostSlotAsync(string fileName, string format, long sizeBytes, CancellationToken cancellationToken)
    {
        var photo = new PhotoUploadRequest(format, sizeBytes);
        ApiResult<PhotoUploadSlot> api = await _handler.CreatePhotoAsync(BusId, photo, cancellationToken);
        if (!api.IsSuccess)
        {
            return new JsonResult(UploadAnswers.ForSlotRefusal(fileName, api));
        }

        PhotoUploadSlot slot = api.Value!;
        if (FleetDisplay.SafePhotoAddress(slot.UploadUrl) is null)
        {
            // An address the browser must not be sent to: free the place again rather than use it.
            await _handler.DeletePhotoAsync(BusId, slot.PhotoId, cancellationToken);
            return new JsonResult(UploadAnswers.Failed(fileName, UploadAnswers.ServerError));
        }

        return new JsonResult(UploadAnswers.Send(slot));
    }

    /// <summary>The script's last step for one file: Fleet checks the stored file and adds it.</summary>
    public async Task<IActionResult> OnPostConfirmAsync(Guid photoId, string fileName, int attempt, CancellationToken cancellationToken)
    {
        ApiResult<BusPhotoItem> api = await _handler.ConfirmPhotoAsync(BusId, photoId, cancellationToken);
        if (api.IsSuccess)
        {
            return new JsonResult(UploadAnswers.Added(fileName));
        }

        if (api.Problem!.Code != UploadAnswers.PhotoNotUploaded)
        {
            return new JsonResult(UploadAnswers.ForConfirmRefusal(fileName, api.Problem!));
        }

        if (attempt < LastConfirmAttempt)
        {
            return new JsonResult(UploadAnswers.AskAgain());
        }

        // The file never arrived: the pending photo would hold a place for minutes, so it goes now.
        await _handler.DeletePhotoAsync(BusId, photoId, cancellationToken);
        return new JsonResult(UploadAnswers.Failed(fileName, UploadAnswers.NotSent));
    }

    /// <summary>The browser could not send the file: its pending photo goes, so the place is free again.</summary>
    public async Task<IActionResult> OnPostDiscardAsync(Guid photoId, string fileName, CancellationToken cancellationToken)
    {
        await _handler.DeletePhotoAsync(BusId, photoId, cancellationToken);
        return new JsonResult(UploadAnswers.Failed(fileName, UploadAnswers.NotSent));
    }

    /// <summary>How many more photos the bus can take.</summary>
    public int FreePlaces()
    {
        return MaxPhotos - Bus!.Photos.Count;
    }

    /// <summary>The address of one of the page's handlers ("Order", "Slot", "Section"...).</summary>
    public string HandlerAddress(string handler)
    {
        return Url.Page(PhotosPage, handler, new { busId = BusId })!;
    }

    /// <summary>The address that asks before a photo is deleted.</summary>
    public string DeleteQuestionAddress(Guid photoId)
    {
        return Url.Page(PhotosPage, new { busId = BusId, delete = photoId })!;
    }

    /// <summary>The photos page itself: the Cancel link of the delete question.</summary>
    public string PageAddress()
    {
        return Url.Page(PhotosPage, new { busId = BusId })!;
    }

    public string BusAddress()
    {
        return $"/operator/buses/{BusId}";
    }

    private async Task ShowBusAsync(CancellationToken cancellationToken)
    {
        ApiResult<PhotosBus> api = await _handler.GetBusAsync(BusId, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = AlertFor(api.Problem!);
            return;
        }

        Bus = api.Value!;
    }

    private async Task<IActionResult> ShowAgainAsync(PageAlert alert, CancellationToken cancellationToken)
    {
        await ShowBusAsync(cancellationToken);
        if (Alert is null)
        {
            Alert = alert;
        }

        return Page();
    }

    private async Task<IActionResult> ShowRefusalAsync(ApiProblem problem, Guid? askedPhotoId, CancellationToken cancellationToken)
    {
        if (FleetDisplay.HidesRecord(problem))
        {
            Alert = AlertFor(problem);
            return Page();
        }

        // Nothing changed, so the photos are read again and shown as they still are.
        await ShowAgainAsync(AlertFor(problem), cancellationToken);
        AskAbout(askedPhotoId);
        return Page();
    }

    private void AskAbout(Guid? photoId)
    {
        if (Bus is null || photoId is null)
        {
            return;
        }

        bool isShown = Bus.Photos.Any(photo => photo.PhotoId == photoId);
        if (isShown)
        {
            ConfirmingPhotoId = photoId;
        }
    }

    private PageAlert AlertFor(ApiProblem problem)
    {
        PageAlert alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        if (problem.Code == FleetDisplay.NotFound)
        {
            return alert.WithLink(BackToListText, BusListAddress);
        }

        return alert;
    }
}
