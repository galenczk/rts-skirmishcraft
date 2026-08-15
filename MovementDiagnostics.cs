using Godot;
using System;

public static class MovementDiagnostics
{
    private static bool? _enabled;

    public static bool Enabled
    {
        get
        {
            if (_enabled.HasValue)
            {
                return _enabled.Value;
            }

            _enabled = Array.Exists(
                OS.GetCmdlineUserArgs(),
                argument => argument.Equals(
                    "--movement-diag",
                    StringComparison.OrdinalIgnoreCase));
            return _enabled.Value;
        }
    }

    public static void Log(string message)
    {
        if (Enabled)
        {
            GD.Print($"DIAG MOVE {message}");
        }
    }
}
