using System.Text.Json;
using Fezd.Contracts;
using Xunit;

namespace Fezd.Contracts.Tests
{
    public class FezdJsonContextTests
    {
        [Fact]
        public void DeployRequest_RoundTripsThroughSourceGenContext()
        {
            var dto = new DeployRequestDto
            {
                ProjectId = "abc123",
                TargetAddress = "192.168.1.10",
                Port = 502,
                Run = true,
                Force = false,
                Driver = "TCPIP"
            };

            string json = JsonSerializer.Serialize(dto, FezdJsonContext.Default.DeployRequestDto);
            Assert.Contains("\"projectId\":\"abc123\"", json);
            Assert.Contains("\"run\":true", json);

            DeployRequestDto back = JsonSerializer.Deserialize(json, FezdJsonContext.Default.DeployRequestDto);
            Assert.Equal(dto.ProjectId, back.ProjectId);
            Assert.Equal(dto.TargetAddress, back.TargetAddress);
            Assert.Equal(502, back.Port);
            Assert.True(back.Run);
        }

        [Fact]
        public void JobResult_And_ErrorEnvelope_RoundTrip()
        {
            JobResultDto ok = JobResultDto.Ok("done");
            string okJson = JsonSerializer.Serialize(ok, FezdJsonContext.Default.JobResultDto);
            JobResultDto okBack = JsonSerializer.Deserialize(okJson, FezdJsonContext.Default.JobResultDto);
            Assert.True(okBack.Success);
            Assert.Equal(0, okBack.ExitCode);

            var err = new ErrorEnvelope("unauthorized", "bad token", FezdExitCodes.UsageError, "req-1");
            string errJson = JsonSerializer.Serialize(err, FezdJsonContext.Default.ErrorEnvelope);
            ErrorEnvelope errBack = JsonSerializer.Deserialize(errJson, FezdJsonContext.Default.ErrorEnvelope);
            Assert.Equal("unauthorized", errBack.Error);
            Assert.Equal("req-1", errBack.RequestId);
            Assert.Equal(FezdExitCodes.UsageError, errBack.ExitCode);
        }

        [Fact]
        public void ExitCodes_ProtectClientServerParity()
        {
            Assert.Equal(0, FezdExitCodes.Ok);
            Assert.Equal(2, FezdExitCodes.UsageError);
            Assert.Equal(5, FezdExitCodes.ConnectivityError);
            Assert.Equal(7, FezdExitCodes.DeployError);
            Assert.Equal(8, FezdExitCodes.TargetBusy);
        }

        [Fact]
        public void AutomationProfile_RoundTripsThroughSourceGenContext()
        {
            var dto = new AutomationProfileDto
            {
                Id = "schneider-control-expert",
                Vendor = "Schneider Electric",
                Toolchain = "EcoStruxure Control Expert",
                DisplayName = "Control Expert gateway",
                ProjectFormats = { ".zef", ".stu" },
                Platforms =
                {
                    new AutomationPlatformDto
                    {
                        Family = "Modicon M340",
                        Prefixes = "BMX P34",
                        Notes = "Primary",
                        Simulator = true,
                        Physical = true
                    }
                },
                Simulator = new SimulatorProfileDto
                {
                    DisplayName = "Control Expert PLC Simulator",
                    Families = { "M340", "M580" }
                },
                Note = "Test."
            };

            string json = JsonSerializer.Serialize(dto, FezdJsonContext.Default.AutomationProfileDto);
            Assert.Contains("\"id\":\"schneider-control-expert\"", json);
            Assert.Contains("\"simulator\":true", json);

            AutomationProfileDto back = JsonSerializer.Deserialize(json, FezdJsonContext.Default.AutomationProfileDto);
            Assert.Equal(dto.Id, back.Id);
            Assert.Equal(dto.Vendor, back.Vendor);
            Assert.Equal("M340", back.Simulator.Families[0]);
            Assert.True(back.Platforms[0].Simulator);
        }
    }
}
