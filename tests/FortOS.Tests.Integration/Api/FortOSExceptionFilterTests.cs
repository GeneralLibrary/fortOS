using FortOS.Api.Filters;
using FortOS.Core;
using Microsoft.AspNetCore.Http;

namespace FortOS.Tests.Integration.Api;

/// <summary>
/// Unit tests for <see cref="FortOSExceptionFilter"/>'s exception→problem-details
/// mapping. Pure function tests: no Linux host required.
/// </summary>
public sealed class FortOSExceptionFilterTests
{
    [Fact]
    public void Map_CommandExecutionException_ExposesStderrInDetail()
    {
        var (status, code, error) = FortOSExceptionFilter.Map(
            new CommandExecutionException("Command execution failed: mount", 32, string.Empty, "mount: mount point does not exist"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("COMMAND_EXECUTION_FAILED", code);
        Assert.Contains("Command execution failed: mount", error);
        Assert.Contains("mount point does not exist", error);
    }

    [Fact]
    public void Map_CommandExecutionException_WithoutStderr_KeepsMessageOnly()
    {
        var (status, code, error) = FortOSExceptionFilter.Map(
            new CommandExecutionException("Command execution failed: mdadm", 1, string.Empty, string.Empty));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("COMMAND_EXECUTION_FAILED", code);
        Assert.Equal("Command execution failed: mdadm", error);
    }

    [Fact]
    public void Map_ArgumentException_IsBadRequest()
    {
        var (status, code, error) = FortOSExceptionFilter.Map(new ArgumentException("bad input"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("INVALID_ARGUMENT", code);
        Assert.Equal("bad input", error);
    }

    [Fact]
    public void Map_FortOSException_Is500WithErrorCode()
    {
        var (status, code, error) = FortOSExceptionFilter.Map(new PlatformException("boom", errorCode: "PLATFORM_ERROR"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("PLATFORM_ERROR", code);
        Assert.Equal("boom", error);
    }

    [Fact]
    public void Map_UnknownException_IsGeneric500()
    {
        var (status, code, error) = FortOSExceptionFilter.Map(new InvalidOperationException("something"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("INTERNAL_ERROR", code);
        Assert.Equal("Internal server error.", error);
    }

    [Fact]
    public void CommandDetail_TruncatesOversizedStderr()
    {
        var longStderr = new string('x', 8000);
        var detail = FortOSExceptionFilter.CommandDetail(
            new CommandExecutionException("Command execution failed: mkfs.ext4", 1, string.Empty, longStderr));

        Assert.Equal(4000, detail.Length);
    }
}
