/// <summary>
/// Turns throttle into drive force and splits it across the driven wheels.
/// Subclass and implement ComputeForce to change where force comes from
/// (a plain engine, one that burns fuel, animals pulling...).
/// </summary>
public abstract class VehiclePower : VehicleModule
{
    public override int Order => VehicleModuleOrder.Power;

    /// <summary>Total drive force (N) for this step. Positive = forward.</summary>
    protected abstract float ComputeForce(float throttle, float forwardSpeed, float dt);

    public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
    {
        float force = ComputeForce(controls.Throttle, Vehicle.ForwardSpeed, dt);

        int driven = 0;
        foreach (var wheel in Vehicle.Wheels)
            if (wheel.IsDriven) driven++;

        float perWheel = driven > 0 ? force / driven : 0f;
        foreach (var wheel in Vehicle.Wheels)
            wheel.DriveForce = wheel.IsDriven ? perWheel : 0f;
    }
}
