using MyHi.Companion.Core.Formatting;
using MyHi.Companion.Core.Ftms;

namespace MyHi.Companion.Tests.Ftms;

public class FtmsCommandsTests
{
    [Fact]
    public void RequestControl_is_opcode_00()
    {
        Assert.Equal("00", HexHelpers.ToHex(FtmsCommands.RequestControl()));
    }

    [Fact]
    public void Reset_is_opcode_01()
    {
        Assert.Equal("01", HexHelpers.ToHex(FtmsCommands.Reset()));
    }

    [Theory]
    // From 05-FTMS-Protocol.md §7 and phase-00-probe-app/TASKS.md 0.7.
    [InlineData(6.5, "02 8A 02")]
    // From 05a-FTMS-Probe-Procedure.md Part D2.
    [InlineData(2.0, "02 C8 00")]
    [InlineData(0.0, "02 00 00")]
    [InlineData(16.0, "02 40 06")]
    public void SetTargetSpeed_encodes_uint16_LE_hundredths(double kph, string expectedHex)
    {
        Assert.Equal(expectedHex, HexHelpers.ToHex(FtmsCommands.SetTargetSpeed(kph)));
    }

    [Fact]
    public void SetTargetSpeed_rejects_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FtmsCommands.SetTargetSpeed(-1));
    }

    [Fact]
    public void StartOrResume_is_opcode_07()
    {
        Assert.Equal("07", HexHelpers.ToHex(FtmsCommands.StartOrResume()));
    }

    [Fact]
    public void Pause_includes_mandatory_parameter_byte()
    {
        Assert.Equal("08 02", HexHelpers.ToHex(FtmsCommands.Pause()));
    }

    [Fact]
    public void Stop_includes_mandatory_parameter_byte()
    {
        Assert.Equal("08 01", HexHelpers.ToHex(FtmsCommands.Stop()));
    }
}

public class ControlPointResponseParserTests
{
    [Theory]
    // 80 00 01 -> (RequestControl, Success)
    [InlineData("80 00 01", FtmsOpCode.RequestControl, FtmsResultCode.Success)]
    // 80 02 03 -> (SetTargetSpeed, InvalidParameter)
    [InlineData("80 02 03", FtmsOpCode.SetTargetSpeed, FtmsResultCode.InvalidParameter)]
    [InlineData("80 07 01", FtmsOpCode.StartOrResume, FtmsResultCode.Success)]
    [InlineData("80 08 05", FtmsOpCode.StopOrPause, FtmsResultCode.ControlNotPermitted)]
    public void TryParse_decodes_known_examples(string hex, FtmsOpCode expectedOp, FtmsResultCode expectedResult)
    {
        var bytes = HexHelpers.FromHex(hex);

        var ok = ControlPointResponseParser.TryParse(bytes, out var response);

        Assert.True(ok);
        Assert.NotNull(response);
        Assert.Equal(0x80, response!.ResponseCode);
        Assert.Equal(expectedOp, response.RequestOpCode);
        Assert.Equal(expectedResult, response.ResultCode);
        Assert.True(expectedResult != FtmsResultCode.Success || response.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("80")]
    [InlineData("80 00")]
    public void TryParse_rejects_short_buffer_without_throwing(string hex)
    {
        var bytes = HexHelpers.FromHex(hex);

        var ok = ControlPointResponseParser.TryParse(bytes, out var response);

        Assert.False(ok);
        Assert.Null(response);
    }

    [Fact]
    public void TryParse_captures_trailing_parameters()
    {
        var bytes = HexHelpers.FromHex("80 02 01 AA BB");

        ControlPointResponseParser.TryParse(bytes, out var response);

        Assert.Equal([0xAA, 0xBB], response!.Parameters);
    }

    [Fact]
    public void Unknown_opcode_and_result_describe_as_unknown_rather_than_throwing()
    {
        var bytes = HexHelpers.FromHex("80 FE FD");

        ControlPointResponseParser.TryParse(bytes, out var response);

        Assert.Null(response!.RequestOpCode);
        Assert.Null(response.ResultCode);
        Assert.Contains("Unknown", response.DescribeRequestOpCode());
        Assert.Contains("Unknown", response.DescribeResultCode());
    }
}
