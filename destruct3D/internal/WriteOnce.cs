using Godot;

namespace Destruct3D;

// just used for permanentLeft / Right fields in VSTNode so that they can only be written to once (and they can't be readonly as they have to be initialised outside of the constructor due to the logic of the bisecting etc)

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
