using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel.Extensions
{
  public static class TaskEx
  {
#pragma warning disable RECS0165 // Asynchronous methods should return a Task instead of void
    public static async void RunAsync(this Task task, Action<Task, Exception> handler = null)
#pragma warning restore RECS0165 // Asynchronous methods should return a Task instead of void
    {
      try
      {
        await task;
      }
      catch (Exception ex)
      {
        handler?.Invoke(task, ex);
      }
    }
  }
}
