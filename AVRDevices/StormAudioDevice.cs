using System;
using System.Text;
using System.Net.Sockets;

namespace HTPCAVRVolume.AVRDevices
{
    class StormAudioDevice : IAVRDevice
    {
        private string _IP;

        public StormAudioDevice(string IP)
        {
            _IP = IP;
        }

        public void VolUp()                    { SendCmd("ssp.vol.up"); }
        public void VolDown()                  { SendCmd("ssp.vol.down"); }
        public void ToggleMute()               { SendCmd("ssp.mute.toggle"); }
        public void PowerOn()                  { SendCmd("ssp.power.on"); }
        public void PowerOff()                 { SendCmd("ssp.power.standby"); }

        private void SendCmd(string cmd)
        {
            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(_IP, 23);
                if (!connectTask.Wait(2000))
                    throw new TimeoutException($"Connection to {_IP}:23 timed out");

                byte[] cmdBytes = Encoding.ASCII.GetBytes(cmd + "\r");
                client.GetStream().Write(cmdBytes, 0, cmdBytes.Length);
            }
            finally
            {
                client.Close();
            }
        }
    }
}
