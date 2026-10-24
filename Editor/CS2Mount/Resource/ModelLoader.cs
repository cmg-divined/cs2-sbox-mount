namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a compiled CS2 model (vmdl_c) as an s&amp;box <see cref="Model"/>.
/// </summary>
internal sealed class ModelLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var model = new CompiledModel( Host.ReadFile( filePath ) );
		return new ModelImporter( Host, model, Path ).Import();
	}
}
