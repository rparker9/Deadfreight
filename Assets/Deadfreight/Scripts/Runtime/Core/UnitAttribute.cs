// Scripts/Runtime/Core/UnitAttribute.cs
using UnityEngine;

namespace Deadfreight.Core
{
    /// <summary>Shows a unit after a number field in the Inspector, e.g. [Unit("m/s")].</summary>
    public sealed class UnitAttribute : PropertyAttribute
    {
        public readonly string Unit;
        public UnitAttribute(string unit) => Unit = unit;
    }
}
