﻿namespace Exrin.Framework
{
    using Abstraction;
    using System.Threading.Tasks;

    public class DisplayService : IDisplayService
    {
        private INavigationContainer _container = null;
        public void Init(INavigationContainer container)
        {
            _container = container;
        }

        public async Task ShowDialog(string title, string message)
        {
            await _container.ShowDialog(new DialogOptions() { Title = title, Message = message });
        }
    }
}
