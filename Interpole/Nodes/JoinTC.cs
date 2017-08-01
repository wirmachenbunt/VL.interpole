#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.Linq;
using VVVV.Nodes;


using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Join Timecode",
                Category = "Clip Animation",
                Help = "get a clip information",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class Join : IPluginEvaluate
    {
        #region fields & pins


        

        [Input("Hours")]
        public IDiffSpread<int> FHours;

        [Input("Minutes")]
        public IDiffSpread<int> FMinutes;

        [Input("Seconds")]
        public IDiffSpread<int> FSeconds;

        [Input("Frames")]
        public IDiffSpread<int> FFrames;

        [Output("Timecode")]
        public ISpread<Interpole.TimeCode> FTimeCode;

        Interpole.Clip myclip = new Interpole.Clip();

        [Import()]
        public ILogger FLogger;
       

       

        #endregion fields & pins


     

   

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {


        }



    }
}
