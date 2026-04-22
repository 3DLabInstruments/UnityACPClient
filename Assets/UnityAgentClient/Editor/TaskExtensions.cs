using System.Threading.Tasks;
using UnityEngine;

namespace UnityAgentClient
{
    internal static class TaskExtensions
    {
        public static void Forget(this Task task)
        {
            task.ContinueWith(x =>
            {
                Debug.LogException(x.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}