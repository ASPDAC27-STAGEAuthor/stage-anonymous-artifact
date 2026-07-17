namespace HardwareSim.Core;

internal static class Phase8AElementwisePacketValidation
{
    public static bool TryValidate(Packet packet, out string reason)
    {
        reason = "";
        if (packet.NumElements <= 0 || packet.Values.Count != packet.NumElements ||
            packet.Values.Any(value => !double.IsFinite(value)))
        {
            reason = "finite numeric values matching a positive NumElements are required";
            return false;
        }
        if (!PrecisionModel.TryGetDigitalBitWidth(packet.Precision, out var bitWidth) ||
            packet.BitWidth != bitWidth || packet.Bits != checked((long)packet.NumElements * bitWidth))
        {
            reason = "Bits and BitWidth must match a concrete digital precision";
            return false;
        }
        if (!Phase8AStageRouteMetadata.TryValidateBoundMetadata(packet, out reason)) return false;
        if (!Phase8ACollectiveMetadataCodec.TryReadOutputRoute(packet, out _, out _, out _, out reason)) return false;
        return true;
    }
}
