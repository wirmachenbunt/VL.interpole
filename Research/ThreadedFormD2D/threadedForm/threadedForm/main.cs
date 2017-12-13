#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using System.Threading;
using System.Windows.Forms;
using threadedForm;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "ThreadedForm",
                Category = "Value",
                Help = "Basic template with one value in/out",
                Tags = "c#",
        AutoEvaluate = false)]
    #endregion PluginInfo
    public class Template : IPluginEvaluate , IDisposable
    {
        #region fields & pins
        [Input("Input", DefaultValue = 1.0)]
        public ISpread<double> FInput;

        [Output("Output")]
        public ISpread<double> FOutput;


        private Thread FThread;
        
        private bool isInit = false;

        

        [Import()]
        public ILogger FLogger;
        #endregion fields & pins

        //called when data for any output pin is requested

        Form1 MyForm;

        public void Evaluate(int SpreadMax)
        {
            FOutput.SliceCount = SpreadMax;

            
            if(!isInit)
            {

                
                                FThread = new Thread(new ThreadStart(ShowForm));
                                FThread.SetApartmentState(ApartmentState.STA);
                                FThread.Start();
                                //FThread = new Thread(new ThreadStart(() => MyForm.ShowDialog()));
                                //new Thread(() => new Form().ShowDialog()).Start();
               

                //MyForm.Show();
               
                isInit = true;
            }

            

            //MyForm.Invalidate();
            for (int i = 0; i < SpreadMax; i++)
                FOutput[i] = FInput[i] * 2;

            //FLogger.Log(LogType.Debug, "hi tty!");
        }

        


        public void ShowForm()
        {
            
            MyForm = new Form1();
            //Application.Run();

        }

        public void Dispose()
        {
            //FThread.Abort();

            if(MyForm != null) MyForm.Dispose();

            //throw new NotImplementedException();

        }
    }
}
