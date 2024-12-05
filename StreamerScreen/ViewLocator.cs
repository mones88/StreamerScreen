using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using StreamerScreen.ViewModels;
using StreamerScreen.Views;

namespace StreamerScreen;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        if (param is AwaitingConnectionViewModel)
            return new AwaitingConnectionView();

        if (param is IdleViewModel)
            return new IdleView();

        if (param is ZoneViewModel)
            return new ZoneView();

        return new TextBlock {Text = "Not Found: " + param.GetType().Name};
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}