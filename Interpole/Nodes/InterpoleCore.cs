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
    [PluginInfo(Name = "Interpole Core",
                Category = "Interpole",
                Help = "",
                Tags = "",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class InterpoleCore : IPluginEvaluate
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


        [Import()]
        public ILogger FLogger;

        #endregion fields & pins


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            






        }
    }
}
