using Microsoft.AspNetCore.Mvc;

namespace FakePaymentProvider.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SimulateProviderController : ControllerBase
    {
        private static readonly Random _random = new();

        public class ChargeRequest
        {
            public long AmountMinorUnits { get; set; }
            public string Currency { get; set; } = "PKR";

            // Testing ke liye — true bhejo to guaranteed failure milegi
            public bool? ForceFail { get; set; }
        }

        public class ChargeResponse
        {
            public bool Success { get; set; }
            public string ProviderReference { get; set; } = string.Empty;
            public string? FailureReason { get; set; }
        }

        [HttpPost("charge")]
        public ActionResult<ChargeResponse> Charge(ChargeRequest request)
        {
            // Simulate karo thoda processing delay (real provider jaisa)
            Thread.Sleep(200);

            var shouldFail = request.ForceFail == true || _random.Next(1, 101) <= 10; // 10% random failure

            if (shouldFail)
            {
                return Ok(new ChargeResponse
                {
                    Success = false,
                    ProviderReference = string.Empty,
                    FailureReason = "Simulated provider decline (insufficient funds / random failure)."
                });
            }

            return Ok(new ChargeResponse
            {
                Success = true,
                ProviderReference = $"PROV-{Guid.NewGuid():N}".Substring(0, 20)
            });
        }
    }
}