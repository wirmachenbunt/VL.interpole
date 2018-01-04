#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "mouseScaleTest", Category = "Value", Help = "Basic template with one value in/out", Tags = "c#")]
	#endregion PluginInfo
	public class ValuemouseScaleTestNode : IPluginEvaluate
	{
		#region fields & pins
		
		[Input("Reset")]
		public ISpread<bool> FReset;
		
		[Input("Mouse")]
		public ISpread<Vector2D> Mouse;
		
		[Input("Wheel")]
		public IDiffSpread<double> Wheel;		

		[Output("PosOut")]
		public ISpread<Vector2D> FPosOut;	
		
		[Output("ScaleOut",DefaultValue = 1.0)]
		public ISpread<double> ZoomOut;

		[Import()]
		public ILogger FLogger;
		#endregion fields & pins

		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			
			if(Wheel.IsChanged)
			{
				double oldzoom = ZoomOut[0];

            if (Wheel[0] > 0)
            {
                ZoomOut[0] += 0.01F;
            }

            else if (Wheel[0] < 0)
            {
                ZoomOut[0] = Math.Max(ZoomOut[0] - 0.01F, 0.01F);
            }
			
			double x = (Mouse[0].x - FPosOut[0].x);    // Where location of the mouse in the pictureframe
            double y = (Mouse[0].y - FPosOut[0].y);
			
			
			double oldimagex = x / oldzoom;  // Where in the IMAGE is it now
            double oldimagey = y / oldzoom;

            double newimagex = x / ZoomOut[0];     // Where in the IMAGE will it be when the new zoom i made
            double newimagey = y / ZoomOut[0];
			
			double posX = FPosOut[0].x;
			
			Vector2D myT = new Vector2D(newimagex - oldimagex + FPosOut[0].x,newimagey - oldimagey + FPosOut[0].y);

            FPosOut[0] = myT;
			}
			
			if(FReset[0])
			{
				FPosOut[0] = new Vector2D(0,0);
				ZoomOut[0] = 1;
			}
			
			
			
			//FLogger.Log(LogType.Debug, "hi tty!");
		}
	}
}
