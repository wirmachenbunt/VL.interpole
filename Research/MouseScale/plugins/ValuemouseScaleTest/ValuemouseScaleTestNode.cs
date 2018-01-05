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
		public ISpread<Matrix4x4> ZoomOut;

		[Import()]
		public ILogger FLogger;
		#endregion fields & pins
		
		double imgx = 0;                        
        double imgy = 0;

        double zoom = 1;

		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			
			if(Wheel.IsChanged)
			{
				double oldzoom = zoom;

	            if (Wheel[0] > 0)
	            {
	                zoom += 0.1F;
	            }
	
	            else if (Wheel[0] < 0)
	            {
	                zoom = Math.Max(zoom - 0.1F, 0.01F);
	            }
				
				double x = Mouse[0].x;    // Where location of the mouse in the pictureframe
	            double y = Mouse[0].y;
				
				
				double oldimagex = x / oldzoom;  // Where in the IMAGE is it now
	            double oldimagey = y / oldzoom;
	
	            double newimagex = x / zoom;     // Where in the IMAGE will it be when the new zoom i made
	            double newimagey = y / zoom;
				
				imgx = newimagex - oldimagex + imgx;
				imgy = newimagey - oldimagey + imgy;

				
	
	            FPosOut[0] = new Vector2D(imgx,imgy);
				ZoomOut[0] = VMath.Scale(zoom,zoom,1);
			}
			
			if(FReset[0])
			{
				FPosOut[0] = new Vector2D(0,0);
				ZoomOut[0] = VMath.Scale(1,1,1);
			}
			
			
			
			//FLogger.Log(LogType.Debug, "hi tty!");
		}
	}
}
