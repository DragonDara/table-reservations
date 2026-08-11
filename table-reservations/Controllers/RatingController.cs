using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class RatingController : ControllerBase
{
    private readonly DgisRatingService _service;

    public RatingController(DgisRatingService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (rating, reviewCount) = await _service.GetRatingAsync();
        return Ok(new { rating, reviewCount });
    }
}