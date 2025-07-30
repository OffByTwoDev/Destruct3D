using Godot;

namespace Destruct3D;

public class WriteOnce<T>
{
	private T _value;
	private bool isSet = false;

	public T Value
	{
		get => _value;
		set
		{
			if (isSet)
            {
                GD.PushWarning($"this variable {typeof(T).Name} cannot be set to twice. returning early");
                return;
            }
			_value = value;
			isSet = true;
		}
	}

	public static implicit operator T(WriteOnce<T> writeOnce) => writeOnce._value;
}
