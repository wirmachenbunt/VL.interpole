#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using SlimDX;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "Camera2D", Category = "Transform", Help = "Basic template with one transform in/out", Tags = "matrix, c#")]
	#endregion PluginInfo
	public class TransformCamera2DNode : IPluginEvaluate
	{
		#region fields & pins
		[Input("Input")]
		public ISpread<Matrix4x4> FInput;
		
		[Input("Scale")]
		public ISpread<Vector3> Scale;
		
		
		[Input("Center")]
		public ISpread<Vector3> Center;
		

		[Output("Output")]
		public ISpread<Matrix> FOutput;

		[Import()]
		public ILogger FLogger;
		#endregion fields & pins
		
		
		

		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			Matrix centerTrans = Matrix.Translation(Center[0]);
			Matrix negCenterTrans = Matrix.Translation(Vector3.Negate(Center[0]));
			Matrix scaling = Matrix.Scaling(Scale[0]);
			
			Matrix result = Matrix.Multiply(centerTrans,scaling);
			
			result = Matrix.Multiply(result,negCenterTrans);
			FOutput[0] = result;
			//FOutput[0] = VMath.Translate(Center[0].x, Center[0].y, Center[0].z);

			//FLogger.Log(LogType.Debug, "Logging to Renderer (TTY)");
		}
	}
}
