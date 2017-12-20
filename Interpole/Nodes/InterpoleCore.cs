#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.Linq;
using VVVV.Nodes;
using Interpole;

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
    public class InterpoleCoreNode : IPluginEvaluate
    {
        #region fields & pins    

        [Input("Play")]
        public IDiffSpread<bool> FPlay;

        [Input("Set Timecode")]
        public IDiffSpread<bool> FSetTimecode;

        [Input("Frame Clock", IsSingle = true)]
        public IDiffSpread<int> FExternalClock;

        [Input("Set Frame Clock")]
        public IDiffSpread<bool> FSetExternalClock;

        [Input("InterpoleIn")]
        public IDiffSpread<InterpoleUI> FInterpoleIn;

        [Output("ClipXML")]
        public ISpread<string> ClipXMLOut;


        [Import()]
        public ILogger FLogger;

        #endregion fields & pins


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {

         



        }
    }
}
