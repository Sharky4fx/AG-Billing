using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AGRechnung.FunctionApp.Tests
{
    public class FunctionsTests
    {
        [Fact]
        public async Task CreateNewUser_ReturnsConflict_WhenEmailExists()
        {
            var repoMock = new Mock<AGRechnung.FunctionApp.Repositories.IAuthRepository>();
            repoMock.Setup(r => r.CreateUserWithVerificationTokenAsync(
                    It.IsAny<string>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<System.DateTime>()))
                .ThrowsAsync(new AGRechnung.FunctionApp.Repositories.EmailAlreadyExistsException("test@example.com"));

            var logger = NullLogger<AGRechnung.CreateNewUser.CreateNewUser>.Instance;
            var func = new AGRechnung.CreateNewUser.CreateNewUser(logger, repoMock.Object);

            var context = new DefaultHttpContext();
            var req = context.Request;
            req.Method = "POST";
            req.ContentType = "application/json";
            var payload = "{\"email\":\"test@example.com\",\"password\":\"Password123\"}";
            var body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            req.Body = body;
            req.ContentLength = body.Length;

            var result = await func.Run(req);

            Assert.IsType<ConflictObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_ReturnsOk_WhenTokenValid()
        {
            var repoMock = new Mock<AGRechnung.FunctionApp.Repositories.IAuthRepository>();
            repoMock.Setup(r => r.VerifyEmailAsync(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var logger = NullLogger<AGRechnung.VerifyEmail.VerifyEmail>.Instance;
            var func = new AGRechnung.VerifyEmail.VerifyEmail(logger, repoMock.Object);

            var context = new DefaultHttpContext();
            var req = context.Request;
            req.QueryString = new QueryString("?userId=123&token=abc123");

            var result = await func.Run(req);

            Assert.IsType<OkObjectResult>(result);
            repoMock.Verify(r => r.VerifyEmailAsync(123, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task VerifyEmail_ReturnsBadRequest_WhenTokenInvalid()
        {
            var repoMock = new Mock<AGRechnung.FunctionApp.Repositories.IAuthRepository>();
            repoMock.Setup(r => r.VerifyEmailAsync(It.IsAny<int>(), It.IsAny<string>()))
                .ThrowsAsync(new AGRechnung.FunctionApp.Repositories.InvalidVerificationTokenException());

            var logger = NullLogger<AGRechnung.VerifyEmail.VerifyEmail>.Instance;
            var func = new AGRechnung.VerifyEmail.VerifyEmail(logger, repoMock.Object);

            var context = new DefaultHttpContext();
            var req = context.Request;
            req.QueryString = new QueryString("?userId=123&token=invalid");

            var result = await func.Run(req);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CleanupUnverifiedUsers_LogsResults()
        {
            var repoMock = new Mock<AGRechnung.FunctionApp.Repositories.IAuthRepository>();
            repoMock.Setup(r => r.CleanupUnverifiedUsersAsync())
                .ReturnsAsync(2); // Simulate 2 users cleaned up

            var logger = NullLogger<AGRechnung.CleanupUnverifiedUsers.CleanupUnverifiedUsers>.Instance;
            var func = new AGRechnung.CleanupUnverifiedUsers.CleanupUnverifiedUsers(logger, repoMock.Object);

            await func.Run(null); // TimerInfo can be null for testing

            repoMock.Verify(r => r.CleanupUnverifiedUsersAsync(), Times.Once);
        }
    }
}
