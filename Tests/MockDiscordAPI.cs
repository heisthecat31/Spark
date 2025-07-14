using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Newtonsoft.Json;

namespace Spark.Tests
{
    public class MockDiscordAPI
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Dictionary<string, string> _responses = new Dictionary<string, string>();

        public MockDiscordAPI(string uri)
        {
            _listener.Prefixes.Add(uri);
        }

        public void Start()
        {
            _listener.Start();
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                string responseString;
                                if (_responses.TryGetValue(ctx.Request.RawUrl, out responseString))
                                {
                                    byte[] buf = System.Text.Encoding.UTF8.GetBytes(responseString);
                                    ctx.Response.ContentLength64 = buf.Length;
                                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                                }
                                else
                                {
                                    ctx.Response.StatusCode = 404;
                                }
                            }
                            catch { } // suppress any exceptions
                            finally
                            {
                                // always close the stream
                                if (ctx != null)
                                {
                                    ctx.Response.OutputStream.Close();
                                }
                            }
                        }, _listener.GetContext());
                    }
                }
                catch { } // suppress any exceptions
            });
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }

        public void AddResponse(string path, object response)
        {
            _responses[path] = JsonConvert.SerializeObject(response);
        }
    }
}
