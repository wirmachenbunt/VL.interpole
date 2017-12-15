using System;
using System.Threading;
using System.ComponentModel.Composition;
using System.Windows.Forms;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using threadedForm;
using VVVV.Core.Logging;

namespace VVVV.Nodes
{
    // Node is set to AutoEvaluate so Evaluate is called each frame no matter what
    [PluginInfo(Name = "ThreadedForm", Category = "Windows", AutoEvaluate = true)]
    public class WindowsThreadedFormNode : IPluginEvaluate, IDisposable
    {
        Thread FUIThread;
        Form1 FMyForm;
        Label FMyLabel;

        [Input("Some Text")]
        ISpread<string> FSomeTextIn;

        [Output("Out")]
        ISpread<int> FOut;

        [Input("Value")]
        IDiffSpread<int> FValue;

        public WindowsThreadedFormNode()
        {
            FUIThread = new Thread(InitializeForm);
            FUIThread.SetApartmentState(ApartmentState.STA);
            //FUIThread.IsBackground = true;
            FUIThread.Start();
        }

        public void Dispose()
        {
            if (FMyForm != null)
                FMyForm.Invoke((Action)CleanupForm);
        }

        void InitializeForm()
        {
            FMyForm = new Form1();
            FMyLabel = new Label() { Text = "Lala" };
            FMyForm.Controls.Add(FMyLabel);
            // Use ShowDialog as it starts its own main loop - using Show alone the window just disappears
            FMyForm.ShowDialog();
        }

        void CleanupForm()
        {
            FMyForm.Dispose();
        }


        void InvalidateForm()
        {
            FMyForm.Invalidate();
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if (FMyForm == null)
                return; // Form not created yet

            //FMyForm.Invoke((Action)InvalidateForm);

            //FMyForm.Invalidate();
            foreach (var value in FSomeTextIn)
            {
                //FMyForm.Invoke((Action)(() => FMyLabel.Text = value));

            }
            FOut[0] = FMyForm.counter;
            //if (FValue.IsChanged) FMyForm.Hallo = FValue[0];
            //FMyForm.Invalidate();
        }
    }
}
