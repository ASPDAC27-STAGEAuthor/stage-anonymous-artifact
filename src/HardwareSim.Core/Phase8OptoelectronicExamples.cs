using System.Globalization;

namespace HardwareSim.Core;

/// <summary>Describes one loadable Phase 8 optoelectronic example project.</summary>
public sealed record Phase8OptoelectronicExample(
    string Id,
    string FileName,
    string DisplayName,
    string Description,
    HardwareGraph Graph);

/// <summary>Builds deterministic mixed electrical/optical examples through the production plugin registry.</summary>
public static class Phase8OptoelectronicExamples
{
    /// <summary>Creates the complete Phase 8 optoelectronic example catalog.</summary>
    public static IReadOnlyList<Phase8OptoelectronicExample> CreateAll() =>
    [
        new("digital-optical-transceiver", "01-digital-optical-transceiver.hardware.json",
            "Digital / Optical Transceiver",
            "Digital source through E/O, explicit waveguide, direct O/E, and an electrical sink.",
            CreateDigitalOpticalTransceiver()),
        new("laser-modulator-detector-front-end", "02-laser-modulator-detector-front-end.hardware.json",
            "Laser / Modulator / Detector Front End",
            "Laser carrier split into an analog detector drive and modulator carrier, followed by PD current into O/E.",
            CreateLaserModulatorDetectorFrontEnd()),
        new("mzi-dual-lane-switch", "03-mzi-dual-lane-switch.hardware.json",
            "MZI Dual-Lane Switch",
            "Two electrical lanes cross an MZI in optical space and merge through an electrical router.",
            CreateMziDualLaneSwitch()),
        new("wdm-two-channel-link", "04-wdm-two-channel-link.hardware.json",
            "Two-Channel WDM Link",
            "Two E/O wavelengths share one explicit waveguide through WDM mux/demux and return to one electrical sink.",
            CreateWdmTwoChannelLink()),
        new("mrr-through-drop-router", "05-mrr-through-drop-router.hardware.json",
            "MRR Through / Drop Router",
            "Two wavelengths are combined, routed by MRR resonance, converted to electrical packets, and merged.",
            CreateMrrThroughDropRouter()),
        new("serdes-optical-transceiver", "06-serdes-optical-transceiver.hardware.json",
            "SerDes Optical Transceiver",
            "64b66b Serializer, E/O and waveguide transport, O/E, then Deserializer/CDR recovery.",
            CreateSerDesOpticalTransceiver()),
        new("optical-tree-reduction-loop", "07-optical-tree-reduction-loop.hardware.json",
            "Optical / Electrical Tree Reduction Loop",
            "Laser/modulator receive path, four-packet router tree reduction, and SerDes optical transmit path.",
            CreateOpticalTreeReductionLoop())
    ];

    /// <summary>Creates a minimal digital-to-optical-to-digital transceiver.</summary>
    public static HardwareGraph CreateDigitalOpticalTransceiver()
    {
        var editor = CreateEditor("digital-optical-transceiver", "Digital / Optical Transceiver",
            "Digital source through E/O, explicit waveguide, direct O/E, and an electrical sink.");
        AddDataSource(editor, "source", "Digital Source", 1, 2, 3);
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo", "E/O Transmitter", 3, 2,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "ch0"),
            (Phase8OpticalParameterKeys.OpticalPowerDbm, "0"));
        AddOptical(editor, Phase8OpticalTypeIds.Link, "waveguide", "SiN Waveguide", 5, 2,
            (Phase8OpticalParameterKeys.WaveguideMaterial, "silicon_nitride"));
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe", "O/E Receiver", 7, 2,
            (Phase8OpticalParameterKeys.ReceiverSensitivityDbm, "-18"));
        AddSink(editor, "sink", "Digital Sink", 9, 2);
        Connect(editor, "l_source_eo", "source", "out", "eo", "digital_in");
        Connect(editor, "l_eo_waveguide", "eo", "optical_out", "waveguide", "optical_in");
        Connect(editor, "l_waveguide_oe", "waveguide", "optical_out", "oe", "optical_in");
        Connect(editor, "l_oe_sink", "oe", "digital_out", "sink", "in");
        return FinalizePhysicalDesign(editor);
    }

    /// <summary>Creates a laser/modulator path with an explicit detector-current receiver front end.</summary>
    public static HardwareGraph CreateLaserModulatorDetectorFrontEnd()
    {
        var editor = CreateEditor("laser-modulator-detector-front-end", "Laser / Modulator / Detector Front End",
            "Laser carrier split into an analog detector drive and modulator carrier, followed by PD current into O/E.");
        AddControlSource(editor, "enable_source", "Laser Enable", 1, 3);
        AddOptical(editor, Phase8OpticalTypeIds.Laser, "laser", "1550 nm Laser", 3, 3,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "ch0"),
            (Phase8OpticalParameterKeys.OpticalPowerDbm, "2"));
        AddOptical(editor, Phase8OpticalTypeIds.Splitter, "splitter", "Carrier Splitter", 5, 3);
        AddOptical(editor, Phase8OpticalTypeIds.Photodetector, "drive_pd", "Drive Photodetector", 7, 2);
        AddOptical(editor, Phase8OpticalTypeIds.Modulator, "modulator", "Electro-Optic Modulator", 9, 3);
        AddOptical(editor, Phase8OpticalTypeIds.Link, "waveguide", "Modulated Waveguide", 11, 3);
        AddOptical(editor, Phase8OpticalTypeIds.Photodetector, "receiver_pd", "Receiver Photodetector", 13, 3);
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe", "Detector-Current O/E", 15, 3);
        AddSink(editor, "sink", "Recovered Digital Sink", 17, 3);
        Connect(editor, "l_enable_laser", "enable_source", "control_out", "laser", "enable");
        Connect(editor, "l_laser_splitter", "laser", "optical_out", "splitter", "optical_in");
        Connect(editor, "l_splitter_drive_pd", "splitter", "optical_out_0", "drive_pd", "optical_in");
        Connect(editor, "l_drive_pd_modulator", "drive_pd", "electrical_out", "modulator", "electrical_drive");
        Connect(editor, "l_splitter_modulator", "splitter", "optical_out_1", "modulator", "optical_carrier_in");
        Connect(editor, "l_modulator_waveguide", "modulator", "optical_out", "waveguide", "optical_in");
        Connect(editor, "l_waveguide_receiver_pd", "waveguide", "optical_out", "receiver_pd", "optical_in");
        Connect(editor, "l_receiver_pd_oe", "receiver_pd", "electrical_out", "oe", "detector_current_in");
        Connect(editor, "l_oe_sink", "oe", "digital_out", "sink", "in");
        return FinalizePhysicalDesign(editor);
    }

    /// <summary>Creates a dual-lane optical MZI switch with electrical ingress and egress.</summary>
    public static HardwareGraph CreateMziDualLaneSwitch()
    {
        var editor = CreateEditor("mzi-dual-lane-switch", "MZI Dual-Lane Switch",
            "Two electrical lanes cross an MZI in optical space and merge through an electrical router.");
        AddDataSource(editor, "source_a", "Electrical Lane A", 1, 2, 2);
        AddDataSource(editor, "source_b", "Electrical Lane B", 1, 4, 2);
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo_a", "E/O Lane A", 3, 2,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "ch0"));
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo_b", "E/O Lane B", 3, 4,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1551"),
            (Phase8OpticalParameterKeys.ChannelId, "ch1"));
        AddOptical(editor, Phase8OpticalTypeIds.MziSwitch, "mzi", "Cross-State MZI", 6, 3,
            (Phase8OpticalParameterKeys.SwitchState, "cross"));
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe_0", "O/E Output 0", 9, 2);
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe_1", "O/E Output 1", 9, 4);
        AddRouter(editor, "egress_router", "Electrical Egress Router", 11, 3);
        AddSink(editor, "sink", "Merged Digital Sink", 13, 3);
        Connect(editor, "l_source_a_eo", "source_a", "out", "eo_a", "digital_in");
        Connect(editor, "l_source_b_eo", "source_b", "out", "eo_b", "digital_in");
        Connect(editor, "l_eo_a_mzi", "eo_a", "optical_out", "mzi", "in_0");
        Connect(editor, "l_eo_b_mzi", "eo_b", "optical_out", "mzi", "in_1");
        Connect(editor, "l_mzi_0_oe", "mzi", "out_0", "oe_0", "optical_in");
        Connect(editor, "l_mzi_1_oe", "mzi", "out_1", "oe_1", "optical_in");
        Connect(editor, "l_oe_0_router", "oe_0", "digital_out", "egress_router", "in");
        Connect(editor, "l_oe_1_router", "oe_1", "digital_out", "egress_router", "in");
        Connect(editor, "l_router_sink", "egress_router", "out", "sink", "in");
        return FinalizePhysicalDesign(editor);
    }

    /// <summary>Creates a fixed two-channel WDM optical link.</summary>
    public static HardwareGraph CreateWdmTwoChannelLink()
    {
        var editor = CreateEditor("wdm-two-channel-link", "Two-Channel WDM Link",
            "Two E/O wavelengths share one explicit waveguide through WDM mux/demux and return to one electrical sink.");
        AddDataSource(editor, "source_0", "Electrical Channel 0", 1, 2, 2);
        AddDataSource(editor, "source_1", "Electrical Channel 1", 1, 4, 2);
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo_0", "1550 nm E/O", 3, 2,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "ch0"));
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo_1", "1551 nm E/O", 3, 4,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1551"),
            (Phase8OpticalParameterKeys.ChannelId, "ch1"));
        AddOptical(editor, Phase8OpticalTypeIds.WdmMux, "mux", "Two-Channel WDM Mux", 6, 3,
            (Phase8OpticalParameterKeys.ChannelCount, "2"),
            (Phase8OpticalParameterKeys.ChannelTableNanometers, "1550,1551"),
            (Phase8OpticalParameterKeys.AllocationMode, "fixed"));
        AddOptical(editor, Phase8OpticalTypeIds.Link, "waveguide", "Shared WDM Waveguide", 8, 3);
        AddOptical(editor, Phase8OpticalTypeIds.WdmDemux, "demux", "Two-Channel WDM Demux", 10, 3,
            (Phase8OpticalParameterKeys.ChannelCount, "2"),
            (Phase8OpticalParameterKeys.ChannelTableNanometers, "1550,1551"),
            (Phase8OpticalParameterKeys.AllocationMode, "fixed"));
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe_0", "O/E Channel 0", 13, 2);
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe_1", "O/E Channel 1", 13, 4);
        AddRouter(editor, "egress_router", "Electrical Egress Router", 15, 3);
        AddSink(editor, "sink", "WDM Digital Sink", 17, 3);
        Connect(editor, "l_source_0_eo", "source_0", "out", "eo_0", "digital_in");
        Connect(editor, "l_source_1_eo", "source_1", "out", "eo_1", "digital_in");
        Connect(editor, "l_eo_0_mux", "eo_0", "optical_out", "mux", "ch0_in");
        Connect(editor, "l_eo_1_mux", "eo_1", "optical_out", "mux", "ch1_in");
        Connect(editor, "l_mux_waveguide", "mux", "wdm_out", "waveguide", "optical_in");
        Connect(editor, "l_waveguide_demux", "waveguide", "optical_out", "demux", "wdm_in");
        Connect(editor, "l_demux_0_oe", "demux", "ch0_out", "oe_0", "optical_in");
        Connect(editor, "l_demux_1_oe", "demux", "ch1_out", "oe_1", "optical_in");
        Connect(editor, "l_oe_0_router", "oe_0", "digital_out", "egress_router", "in");
        Connect(editor, "l_oe_1_router", "oe_1", "digital_out", "egress_router", "in");
        Connect(editor, "l_router_sink", "egress_router", "out", "sink", "in");
        return FinalizePhysicalDesign(editor);
    }

    /// <summary>Creates a two-wavelength MRR through/drop routing example.</summary>
    public static HardwareGraph CreateMrrThroughDropRouter()
    {
        var editor = CreateEditor("mrr-through-drop-router", "MRR Through / Drop Router",
            "Two wavelengths are combined, routed by MRR resonance, converted to electrical packets, and merged.");
        AddDataSource(editor, "source_drop", "Resonant Electrical Source", 1, 2, 2);
        AddDataSource(editor, "source_through", "Nonresonant Electrical Source", 1, 4, 2);
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo_drop", "1550 nm E/O", 3, 2,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "ch0"));
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo_through", "1552 nm E/O", 3, 4,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1552"),
            (Phase8OpticalParameterKeys.ChannelId, "ch1"));
        AddOptical(editor, Phase8OpticalTypeIds.Combiner, "combiner", "Optical Combiner", 6, 3);
        AddOptical(editor, Phase8OpticalTypeIds.Link, "waveguide", "MRR Feed Waveguide", 8, 3);
        AddOptical(editor, Phase8OpticalTypeIds.MrrRouter, "mrr", "1550 nm MRR Router", 10, 3,
            (Phase8OpticalParameterKeys.SwitchState, "auto"),
            (Phase8OpticalParameterKeys.NominalResonanceNanometers, "1550"),
            (Phase8OpticalParameterKeys.PassbandNanometers, "0.8"));
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe_through", "Through O/E", 13, 2);
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe_drop", "Drop O/E", 13, 4);
        AddRouter(editor, "egress_router", "Electrical Egress Router", 15, 3);
        AddSink(editor, "sink", "MRR Digital Sink", 17, 3);
        Connect(editor, "l_source_drop_eo", "source_drop", "out", "eo_drop", "digital_in");
        Connect(editor, "l_source_through_eo", "source_through", "out", "eo_through", "digital_in");
        Connect(editor, "l_eo_drop_combiner", "eo_drop", "optical_out", "combiner", "optical_in_0");
        Connect(editor, "l_eo_through_combiner", "eo_through", "optical_out", "combiner", "optical_in_1");
        Connect(editor, "l_combiner_waveguide", "combiner", "optical_out", "waveguide", "optical_in");
        Connect(editor, "l_waveguide_mrr", "waveguide", "optical_out", "mrr", "optical_in");
        Connect(editor, "l_mrr_through_oe", "mrr", "through_out", "oe_through", "optical_in");
        Connect(editor, "l_mrr_drop_oe", "mrr", "drop_out", "oe_drop", "optical_in");
        Connect(editor, "l_oe_through_router", "oe_through", "digital_out", "egress_router", "in");
        Connect(editor, "l_oe_drop_router", "oe_drop", "digital_out", "egress_router", "in");
        Connect(editor, "l_router_sink", "egress_router", "out", "sink", "in");
        return FinalizePhysicalDesign(editor);
    }
    /// <summary>Creates a complete digital Serializer/Deserializer path around an optical transceiver.</summary>
    public static HardwareGraph CreateSerDesOpticalTransceiver()
    {
        var editor = CreateEditor("serdes-optical-transceiver", "SerDes Optical Transceiver",
            "64b66b Serializer, E/O and waveguide transport, O/E, then Deserializer/CDR recovery.");
        AddDataSource(editor, "source", "Parallel Packet Source", 1, 2, 2);
        AddInterface(editor, Phase8SerDesTypeIds.Serializer, "serializer", "64b66b Serializer", 3, 2,
            (Phase8SerDesKeys.LaneCount, "4"),
            (Phase8SerDesKeys.LaneRateBitsPerCycle, "32"),
            (Phase8SerDesKeys.Encoding, "64b66b"));
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "eo", "E/O Transmitter", 5, 2,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "ch0"),
            (Phase8OpticalParameterKeys.OpticalPowerDbm, "0"));
        AddOptical(editor, Phase8OpticalTypeIds.Link, "waveguide", "SiN Serial Waveguide", 7, 2,
            (Phase8OpticalParameterKeys.WaveguideMaterial, "silicon_nitride"));
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "oe", "O/E Receiver", 9, 2,
            (Phase8OpticalParameterKeys.ReceiverSensitivityDbm, "-18"));
        AddInterface(editor, Phase8SerDesTypeIds.Deserializer, "deserializer", "Deserializer / CDR", 11, 2,
            (Phase8SerDesKeys.LaneCount, "4"),
            (Phase8SerDesKeys.LaneRateBitsPerCycle, "32"),
            (Phase8SerDesKeys.Encoding, "64b66b"));
        AddSink(editor, "sink", "Recovered Parallel Sink", 13, 2);

        Connect(editor, "l_source_serializer", "source", "out", "serializer", "parallel_in");
        var serializedTransport = Connect(editor, "l_serializer_eo", "serializer", "serial_out", "eo", "digital_in");
        serializedTransport.BandwidthBitsPerCycle = 128;
        serializedTransport.Parameters[Phase8SerDesKeys.SerializationOwner] = "link";
        serializedTransport.Parameters["transport_payload"] = "encoded_bits";
        Connect(editor, "l_eo_waveguide", "eo", "optical_out", "waveguide", "optical_in");
        Connect(editor, "l_waveguide_oe", "waveguide", "optical_out", "oe", "optical_in");
        Connect(editor, "l_oe_deserializer", "oe", "digital_out", "deserializer", "serial_in");
        Connect(editor, "l_deserializer_sink", "deserializer", "parallel_out", "sink", "in");
        return FinalizePhysicalDesign(editor);
    }


    /// <summary>Creates a complete optical receive, electrical tree reduction, and optical transmit pipeline.</summary>
    public static HardwareGraph CreateOpticalTreeReductionLoop()
    {
        var editor = CreateEditor(
            "optical-tree-reduction-loop",
            "Optical / Electrical Tree Reduction Loop",
            "Laser/modulator receive path, four-packet router tree reduction, then SerDes-driven optical transmit path.");

        AddControlSource(editor, "laser_enable", "Laser Enable", 1, 1);
        AddOptical(editor, Phase8OpticalTypeIds.Laser, "rx_laser", "RX 1550 nm Laser", 3, 3,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "rx_ch0"),
            (Phase8OpticalParameterKeys.OpticalPowerDbm, "3"));
        AddOptical(editor, Phase8OpticalTypeIds.Splitter, "rx_splitter", "RX Carrier Splitter", 6, 3);
        AddOptical(editor, Phase8OpticalTypeIds.Photodetector, "rx_drive_pd", "Modulator Drive PD", 8, 1);
        AddOptical(editor, Phase8OpticalTypeIds.Modulator, "rx_modulator", "RX Modulator", 10, 3,
            (Phase8OpticalParameterKeys.LatencyCycles, "1"));
        AddOptical(editor, Phase8OpticalTypeIds.Link, "rx_waveguide", "RX SiN Waveguide", 13, 3,
            (Phase8OpticalParameterKeys.WaveguideMaterial, "silicon_nitride"));
        var rxMrr = AddOptical(editor, Phase8OpticalTypeIds.MrrRouter, "rx_mrr", "RX Through MRR", 16, 3,
            (Phase8OpticalParameterKeys.SwitchState, "through"),
            (Phase8OpticalParameterKeys.NominalResonanceNanometers, "1550"));
        rxMrr.FindPort("drop_out")!.Required = false;
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "rx_oe", "RX O/E", 19, 3,
            (Phase8OpticalParameterKeys.ReceiverSensitivityDbm, "-18"));
        AddInterface(editor, Phase8SerDesTypeIds.Deserializer, "rx_deserializer", "RX Deserializer / CDR", 22, 3,
            (Phase8SerDesKeys.Encoding, "raw"),
            (Phase8SerDesKeys.LaneCount, "4"),
            (Phase8SerDesKeys.LaneRateBitsPerCycle, "32"));

        var aux1 = AddDataSource(editor, "aux_source_1", "Electrical Partial 1", 18, 6, 1);
        var aux2 = AddDataSource(editor, "aux_source_2", "Electrical Partial 2", 18, 8, 1);
        var aux3 = AddDataSource(editor, "aux_source_3", "Electrical Partial 3", 18, 10, 1);
        SetParameters(aux1, ("payload_value", "2"));
        SetParameters(aux2, ("payload_value", "3"));
        SetParameters(aux3, ("payload_value", "4"));

        AddRouter(editor, "tree_leaf_0", "Tree Router Leaf 0", 25, 4);
        AddRouter(editor, "tree_leaf_1", "Tree Router Leaf 1", 25, 8);
        AddRouter(editor, "tree_root", "Tree Router Root", 29, 6);
        var reduction = editor.AddComponent(ComponentKind.ReductionUnit, "reduction_sum", new GridPosition(33, 6), "Four-Way Reduction Sum");
        SetParameters(reduction,
            ("num_inputs", "4"),
            ("accumulate_latency", "2"),
            ("queue_capacity", "8"));

        AddInterface(editor, Phase8SerDesTypeIds.Serializer, "tx_serializer", "TX 64b66b Serializer", 37, 6,
            (Phase8SerDesKeys.Encoding, "64b66b"),
            (Phase8SerDesKeys.LaneCount, "4"),
            (Phase8SerDesKeys.LaneRateBitsPerCycle, "32"));
        AddOptical(editor, Phase8OpticalTypeIds.EoConverter, "tx_eo", "TX E/O", 40, 6,
            (Phase8OpticalParameterKeys.WavelengthNanometers, "1550"),
            (Phase8OpticalParameterKeys.ChannelId, "tx_ch0"),
            (Phase8OpticalParameterKeys.OpticalPowerDbm, "1"));
        var txMrr = AddOptical(editor, Phase8OpticalTypeIds.MrrRouter, "tx_mrr", "TX Through MRR", 43, 6,
            (Phase8OpticalParameterKeys.SwitchState, "through"),
            (Phase8OpticalParameterKeys.NominalResonanceNanometers, "1550"));
        txMrr.FindPort("drop_out")!.Required = false;
        AddOptical(editor, Phase8OpticalTypeIds.Link, "tx_waveguide", "TX SiN Waveguide", 46, 6,
            (Phase8OpticalParameterKeys.WaveguideMaterial, "silicon_nitride"));
        AddOptical(editor, Phase8OpticalTypeIds.Photodetector, "tx_pd", "TX Photodetector", 49, 6);
        AddOptical(editor, Phase8OpticalTypeIds.OeConverter, "tx_oe", "TX Detector-Current O/E", 52, 6,
            (Phase8OpticalParameterKeys.ReceiverSensitivityDbm, "-18"));
        AddSink(editor, "sink", "Reduced Result Sink", 55, 6);

        Connect(editor, "l_enable_laser", "laser_enable", "control_out", "rx_laser", "enable");
        Connect(editor, "l_laser_splitter", "rx_laser", "optical_out", "rx_splitter", "optical_in");
        Connect(editor, "l_splitter_drive_pd", "rx_splitter", "optical_out_0", "rx_drive_pd", "optical_in");
        Connect(editor, "l_drive_pd_modulator", "rx_drive_pd", "electrical_out", "rx_modulator", "electrical_drive");
        Connect(editor, "l_splitter_modulator", "rx_splitter", "optical_out_1", "rx_modulator", "optical_carrier_in");
        Connect(editor, "l_modulator_rx_waveguide", "rx_modulator", "optical_out", "rx_waveguide", "optical_in");
        Connect(editor, "l_rx_waveguide_mrr", "rx_waveguide", "optical_out", "rx_mrr", "optical_in");
        Connect(editor, "l_rx_mrr_oe", "rx_mrr", "through_out", "rx_oe", "optical_in");
        Connect(editor, "l_rx_oe_deserializer", "rx_oe", "digital_out", "rx_deserializer", "serial_in");

        Connect(editor, "l_rx_deserializer_leaf0", "rx_deserializer", "parallel_out", "tree_leaf_0", "in");
        Connect(editor, "l_aux1_leaf0", "aux_source_1", "out", "tree_leaf_0", "in");
        Connect(editor, "l_aux2_leaf1", "aux_source_2", "out", "tree_leaf_1", "in");
        Connect(editor, "l_aux3_leaf1", "aux_source_3", "out", "tree_leaf_1", "in");
        Connect(editor, "l_leaf0_root", "tree_leaf_0", "out", "tree_root", "in");
        Connect(editor, "l_leaf1_root", "tree_leaf_1", "out", "tree_root", "in");
        Connect(editor, "l_root_reduction", "tree_root", "out", "reduction_sum", "in");
        Connect(editor, "l_reduction_serializer", "reduction_sum", "out", "tx_serializer", "parallel_in");

        var serializedTransport = Connect(editor, "l_serializer_eo", "tx_serializer", "serial_out", "tx_eo", "digital_in");
        serializedTransport.BandwidthBitsPerCycle = 128;
        serializedTransport.Parameters[Phase8SerDesKeys.SerializationOwner] = "link";
        serializedTransport.Parameters["transport_payload"] = "encoded_bits";
        Connect(editor, "l_tx_eo_mrr", "tx_eo", "optical_out", "tx_mrr", "optical_in");
        Connect(editor, "l_tx_mrr_waveguide", "tx_mrr", "through_out", "tx_waveguide", "optical_in");
        Connect(editor, "l_tx_waveguide_pd", "tx_waveguide", "optical_out", "tx_pd", "optical_in");
        Connect(editor, "l_tx_pd_oe", "tx_pd", "electrical_out", "tx_oe", "detector_current_in");
        Connect(editor, "l_tx_oe_sink", "tx_oe", "digital_out", "sink", "in");

        var graph = FinalizePhysicalDesign(editor);
        graph.Parameters["symbol_layout"] = "left_to_right_optical_rx__tree_router_reduction__optical_tx";
        graph.Parameters["electrical_domain"] = "four_packet_tree_router_reduction_sum";
        AddVisualGroup(
            graph,
            "group_optical_rx",
            "Optical Receive Front-End",
            ["laser_enable", "rx_laser", "rx_splitter", "rx_drive_pd", "rx_modulator", "rx_waveguide", "rx_mrr", "rx_oe"],
            "rgba(168,85,247,0.10)",
            "rgba(196,181,253,0.90)",
            "optical_rx");
        AddVisualGroup(
            graph,
            "group_electrical_tree",
            "Electrical Tree Router + Reduction Sum",
            ["rx_deserializer", "aux_source_1", "aux_source_2", "aux_source_3", "tree_leaf_0", "tree_leaf_1", "tree_root", "reduction_sum", "tx_serializer"],
            "rgba(14,165,233,0.09)",
            "rgba(56,189,248,0.90)",
            "electrical_tree_reduction");
        AddVisualGroup(
            graph,
            "group_optical_tx",
            "Optical Transmit + Detection",
            ["tx_eo", "tx_mrr", "tx_waveguide", "tx_pd", "tx_oe", "sink"],
            "rgba(244,63,94,0.08)",
            "rgba(251,113,133,0.90)",
            "optical_tx");
        return graph;
    }

    private static HardwareEditor CreateEditor(string id, string displayName, string description)
    {
        var editor = new HardwareEditor(componentRegistry: ComponentTypeRegistry.CreateDefault());
        editor.Graph.Parameters["example_id"] = id;
        editor.Graph.Parameters["example_display_name"] = displayName;
        editor.Graph.Parameters["example_description"] = description;
        editor.Graph.Parameters["example_phase"] = "8";
        editor.Graph.Parameters["example_model_boundary"] = "synthetic_functional_not_silicon_characterization";
        return editor;
    }

    private static HardwareComponent AddDataSource(HardwareEditor editor, string id, string name, int x, int y, int packetCount)
    {
        var component = editor.AddComponent(ComponentKind.WorkloadSource, id, new GridPosition(x, y), name);
        SetParameters(component,
            ("packet_count", packetCount.ToString(CultureInfo.InvariantCulture)),
            ("inject_count", packetCount.ToString(CultureInfo.InvariantCulture)),
            ("packet_bits", "128"),
            ("inject_interval", "1"));
        return component;
    }

    private static HardwareComponent AddControlSource(HardwareEditor editor, string id, string name, int x, int y)
    {
        var component = AddDataSource(editor, id, name, x, y, 1);
        SetParameters(component, ("source_port", "control_out"), ("payload_value", "1"), ("packet_bits", "32"));
        component.FindPort("out")!.Required = false;
        return component;
    }

    private static HardwareComponent AddOptical(
        HardwareEditor editor,
        string typeId,
        string id,
        string name,
        int x,
        int y,
        params (string Key, string Value)[] parameters)
    {
        var component = editor.AddComponent(typeId, id, new GridPosition(x, y), name);
        SetParameters(component, parameters);
        return component;
    }
    private static HardwareComponent AddInterface(
        HardwareEditor editor,
        string typeId,
        string id,
        string name,
        int x,
        int y,
        params (string Key, string Value)[] parameters)
    {
        var component = editor.AddComponent(typeId, id, new GridPosition(x, y), name);
        SetParameters(component, parameters);
        return component;
    }


    private static HardwareComponent AddRouter(HardwareEditor editor, string id, string name, int x, int y)
    {
        var component = editor.AddComponent(ComponentKind.Router, id, new GridPosition(x, y), name);
        SetParameters(component, ("queue_capacity", "8"), ("routing_policy", "round_robin"), ("arbitration_policy", "round_robin"));
        return component;
    }

    private static HardwareComponent AddSink(HardwareEditor editor, string id, string name, int x, int y) =>
        editor.AddComponent(ComponentKind.WorkloadSink, id, new GridPosition(x, y), name);

    private static void SetParameters(HardwareComponent component, params (string Key, string Value)[] parameters)
    {
        foreach (var (key, value) in parameters) component.Parameters[key] = value;
    }

    private static HardwareLink Connect(
        HardwareEditor editor,
        string linkId,
        string sourceComponentId,
        string sourcePortName,
        string destinationComponentId,
        string destinationPortName)
    {
        var link = editor.Connect(linkId, new PortRef(sourceComponentId, sourcePortName),
            new PortRef(destinationComponentId, destinationPortName));
        var sourcePort = editor.Graph.FindPort(link.Source)
            ?? throw new InvalidOperationException($"Missing source port for example link '{linkId}'.");
        var destinationPort = editor.Graph.FindPort(link.Destination)
            ?? throw new InvalidOperationException($"Missing destination port for example link '{linkId}'.");
        var optical = sourcePort.SignalType == SignalType.Optical && destinationPort.SignalType == SignalType.Optical;
        link.RouteType = optical ? "optical" : "electrical";
        link.EnergyPerBit = optical ? 0.001 : 0.01;
        link.Parameters["example_medium"] = optical ? "optical_waveguide" : "electrical_metal";
        return link;
    }

    private static void AddVisualGroup(
        HardwareGraph graph,
        string id,
        string name,
        IReadOnlyList<string> componentIds,
        string fill,
        string border,
        string layoutRole)
    {
        var metadata = VisualGroupDefaults.CreateMetadata();
        metadata["fill"] = fill;
        metadata["border"] = border;
        metadata["layout_role"] = layoutRole;
        graph.Groups.Add(new VisualGroup
        {
            Id = id,
            Name = name,
            ComponentIds = componentIds.ToList(),
            Collapsed = false,
            VisualMetadata = metadata
        });
    }

    private static HardwareGraph FinalizePhysicalDesign(HardwareEditor editor)
    {
        var graph = editor.Graph;
        var placement = new PhysicalPlacement
        {
            Rows = graph.Components.Max(component => component.Position.Y) + 2,
            Cols = graph.Components.Max(component => component.Position.X) + 2,
            CellWidthMicrometers = 1000,
            CellHeightMicrometers = 1000,
            Layer = "M0",
            FloorplanMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["example_phase"] = "8",
                ["electrical_layer"] = "M3",
                ["optical_layer"] = "WG"
            }
        };
        foreach (var component in graph.Components.OrderBy(component => component.Id, StringComparer.Ordinal))
        {
            var result = placement.PlaceComponent(component.Id, component.Position.Y, component.Position.X, layer: "M0");
            if (!result.IsSuccess)
                throw new InvalidOperationException(string.Join("; ", result.Issues.Select(issue => issue.Message)));
        }

        var routing = new PhysicalRouting();
        foreach (var link in graph.Links.OrderBy(link => link.Id, StringComparer.Ordinal))
        {
            var source = graph.FindComponent(link.Source.ComponentId)
                ?? throw new InvalidOperationException($"Missing source component for example link '{link.Id}'.");
            var destination = graph.FindComponent(link.Destination.ComponentId)
                ?? throw new InvalidOperationException($"Missing destination component for example link '{link.Id}'.");
            var sourcePort = graph.FindPort(link.Source)
                ?? throw new InvalidOperationException($"Missing source port for example link '{link.Id}'.");
            var destinationPort = graph.FindPort(link.Destination)
                ?? throw new InvalidOperationException($"Missing destination port for example link '{link.Id}'.");
            var optical = sourcePort.SignalType == SignalType.Optical && destinationPort.SignalType == SignalType.Optical;
            routing.Routes.Add(new PhysicalRoute
            {
                LinkId = link.Id,
                TargetKind = PhysicalRouteTargetKind.LogicalLink,
                Medium = optical ? RoutingMedium.OpticalWaveguide : RoutingMedium.ElectricalMetal,
                LayerId = optical
                    ? new RoutingLayerId { Stack = "WG", Index = 0, Purpose = "signal" }
                    : RoutingLayerId.Metal(3, "signal"),
                Path = OrthogonalPath(placement.PositionFor(source), placement.PositionFor(destination))
            });
        }

        graph.Placement = placement;
        graph.Routing = routing;
        return graph;
    }

    private static List<PhysicalPoint> OrthogonalPath(PhysicalPoint source, PhysicalPoint destination)
    {
        if (Math.Abs(source.X - destination.X) < 0.000000001 ||
            Math.Abs(source.Y - destination.Y) < 0.000000001)
            return [source, destination];

        var middleX = (source.X + destination.X) / 2.0;
        return [source, new PhysicalPoint(middleX, source.Y), new PhysicalPoint(middleX, destination.Y), destination];
    }
}
