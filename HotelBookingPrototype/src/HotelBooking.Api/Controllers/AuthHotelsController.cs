using HotelBooking.Api.Dtos;
using HotelBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelBooking.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("register")]
    [AllowAnonymous]
    public IActionResult Register([FromBody] RegisterRequest req) => Ok(new { status = "SUCCESS", data = _auth.Register(req) });

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public IActionResult VerifyOtp([FromBody] OtpRequest req) => Ok(new { status = "SUCCESS", data = _auth.VerifyOtp(req) });

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public IActionResult ResendOtp([FromBody] ResendOtpRequest req) => Ok(new { status = "SUCCESS", data = _auth.ResendOtp(req) });

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest req) => Ok(new { status = "SUCCESS", data = _auth.Login(req) });

    [HttpPost("refresh")]
    [AllowAnonymous]
    public IActionResult Refresh([FromBody] RefreshRequest req) => Ok(new { status = "SUCCESS", data = _auth.Refresh(req) });

    [HttpPost("logout")]
    [AllowAnonymous]
    public IActionResult Logout([FromBody] LogoutRequest? req)
    {
        // Access token di header Authorization ikut dicabut agar sesi langsung tidak berlaku (UAT-LOG-06).
        var header = Request.Headers["Authorization"].ToString();
        var accessToken = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        return Ok(new { status = "SUCCESS", data = _auth.Logout(req, accessToken) });
    }
}

[ApiController]
[Route("api/v1/hotels")]
public class HotelsController : ControllerBase
{
    private readonly HotelService _hotels;
    public HotelsController(HotelService hotels) => _hotels = hotels;

    [HttpGet("search")]
    [AllowAnonymous]
    public IActionResult Search([FromQuery] string? destination, [FromQuery] DateOnly? checkIn,
        [FromQuery] DateOnly? checkOut, [FromQuery] int? guestCount)
        => Ok(new { status = "SUCCESS", data = _hotels.Search(destination, checkIn, checkOut, guestCount) });

    [HttpGet("{hotelId}")]
    [AllowAnonymous]
    public IActionResult Get(string hotelId, [FromQuery] DateOnly? checkIn, [FromQuery] DateOnly? checkOut,
        [FromQuery] int? guestCount)
        => Ok(new { status = "SUCCESS", data = _hotels.Get(hotelId, checkIn, checkOut, guestCount) });
}
