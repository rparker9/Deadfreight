/// <summary>
/// Anything that can drive a vehicle: a seated player, an AI, a scripted event.
/// Register with Vehicle.ServerSetControlSource; the vehicle asks for controls every physics step.
/// </summary>
public interface IVehicleControlSource
{
    VehicleControls GetControls();
}
