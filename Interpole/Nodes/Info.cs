#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;


using VVVV.Core.Logging;
using Interpole;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Info",
                Category = "Interpole",
                Help = "get all informations from clip list",
                Tags = "c#",
        AutoEvaluate = true)]
    #endregion PluginInfo
    public class InterpoleInfo : IPluginEvaluate
    {
        #region fields & pins

        [Input("Interpole")]
        public ISpread<InterpoleCoreNode> InterpoleIn;

        [Input("Update", IsBang = true)]
        public IDiffSpread<bool> FUpdate;

        [Output("ClipNames")]
        public ISpread<string> FNames;

        [Output("StartFrame")]
        public ISpread<int> FStartFrame;

        [Output("Duration")]
        public ISpread<int> FDuration;

        [Output("Color")]
        public ISpread<string> FColor;

        [Import()]
        public ILogger FLogger;

        
        #endregion fields & pins

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if(InterpoleIn[0] != null && FUpdate.IsChanged && FUpdate[0])
            {
                FNames.SliceCount = 0;
                FStartFrame.SliceCount = 0;
                FDuration.SliceCount = 0;
                FColor.SliceCount = 0;

                foreach (InterpoleBase.Clip myclip in InterpoleIn[0].clipList)
                {
                    FNames.Add(myclip.Name);
                    FStartFrame.Add(myclip.StartFrame);
                    FDuration.Add(myclip.Duration);
                    FColor.Add(myclip.Color);
                }
            }

        }
    }
}
