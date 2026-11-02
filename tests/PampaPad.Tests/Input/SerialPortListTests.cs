using PampaPad.Core;

namespace PampaPad.Tests;

// La lista de Conexión con la consola, armada con lo que el registro de Windows de esta PC decía el 2026-11-02 (E8b-4).
public class SerialPortListTests
{
    private static readonly SerialPortEntry[] Registry =
    [
        new(@"ACPI\PNP0501\0", "COM1", "Puerto de comunicaciones (COM1)", "@msports.inf,%*pnp0500.devicedesc%;Puerto de comunicaciones"),
        new(@"USB\VID_1A86&PID_55D3\5ABA058177", "COM20", "USB-Enhanced-SERIAL CH343 (COM20)", "@oem15.inf,%ch343ser.devicedesc%;USB-Enhanced-SERIAL CH343"),
        new(@"USB\VID_1A86&PID_55D4\5829059121", "COM8", "USB-Enhanced-SERIAL CH9102 (COM8)", "@oem15.inf,%ch9102ser.devicedesc%;USB-Enhanced-SERIAL CH9102"),
        new(@"BTHENUM\{00001101-0000-1000-8000-00805f9b34fb}_VID&000105d6_PID&000a\8&1b054f6b&0&24B2313F282A_C00000000", "COM6", "Serie estándar sobre el vínculo Bluetooth (COM6)", null),
        new(@"USB\VID_303A&PID_1001&MI_00\7&abd632e&0&0000", "COM21", null, "@usbser.inf,%usbserial.devicedesc%;Dispositivo serie USB"),
    ];

    [Fact]
    public void Only_the_ports_that_are_there_are_listed_in_order_of_their_number()
    {
        IReadOnlyList<ConsolePortView> ports = SerialPortList.Build(["COM20", "COM8", "COM1"], Registry);

        Assert.Equal(new[] { "COM1", "COM8", "COM20" }, ports.Select(p => p.Port));
    }

    [Fact]
    public void The_description_is_the_friendly_name_without_its_port_and_a_usb_port_carries_vid_and_pid()
    {
        ConsolePortView console = Assert.Single(SerialPortList.Build(["COM20"], Registry));

        Assert.Equal(new ConsolePortView("COM20", "USB-Enhanced-SERIAL CH343", "1A86:55D3"), console);
    }

    [Fact]
    public void Without_a_friendly_name_the_description_is_the_text_after_the_last_semicolon_of_the_device_description()
    {
        Assert.Equal("Dispositivo serie USB", Assert.Single(SerialPortList.Build(["COM21"], Registry)).Description);
    }

    [Fact]
    public void A_port_that_is_not_usb_has_no_usb_id_even_if_its_key_says_vid()
    {
        // La clave del Bluetooth trae VID&...: no es un identificador USB, y "Automático" de la etapa 9 no lo tiene que probar.
        IReadOnlyList<ConsolePortView> ports = SerialPortList.Build(["COM1", "COM6"], Registry);

        Assert.All(ports, p => Assert.Null(p.UsbId));
        Assert.Equal("Puerto de comunicaciones", ports[0].Description);
    }

    [Fact]
    public void A_port_that_is_there_but_not_in_the_registry_is_listed_without_a_description()
    {
        Assert.Equal(new ConsolePortView("COM99", "", null), Assert.Single(SerialPortList.Build(["COM99"], Registry)));
    }

    [Fact]
    public void With_no_ports_the_list_is_empty()
    {
        Assert.Empty(SerialPortList.Build([], Registry));
    }
}
