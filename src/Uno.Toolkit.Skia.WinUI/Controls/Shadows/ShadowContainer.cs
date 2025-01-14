﻿using System;
using System.Linq;

#if IS_WINUI
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp.Views.Windows;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using SkiaSharp.Views.UWP;
#endif

#if __ANDROID__
using Android.Views;
#endif

namespace Uno.Toolkit.UI;

/// <summary>
/// Provides the possibility to add many-colored shadows to its content.
/// </summary>
/// <remarks>
/// For now it renders badly on WASM due to a bug on the wasm skiasharp construction of the SKXamlCanvas.
/// It should be fixed when this PR will be merged: https://github.com/mono/SkiaSharp/pull/2443
/// </remarks>
[TemplatePart(Name = nameof(PART_Canvas), Type = typeof(Canvas))]
public partial class ShadowContainer : ContentControl
{
	private const string PART_Canvas = "PART_Canvas";

	private Canvas? _canvas;

#if false // ANDROID (see comment below)
    private readonly SKSwapChainPanel _shadowHost;
    private bool _notOpaqueSet = false;
#else
	private SKXamlCanvas? _shadowHost;
#endif

	private static readonly ShadowsCache Cache = new ShadowsCache();

	private FrameworkElement? _currentContent;

	private CornerRadius _cornerRadius;

	public ShadowContainer()
	{
		DefaultStyleKey = typeof(ShadowContainer);

		_cornerRadius = new CornerRadius(0);

		Loaded += ShadowContainerLoaded;
		Unloaded += ShadowContainerUnloaded;
	}

	private void ShadowContainerUnloaded(object sender, RoutedEventArgs e)
	{
		RevokeListeners();
	}

	private void ShadowContainerLoaded(object sender, RoutedEventArgs e)
	{
		UpdateShadows();
	}

	private void RevokeListeners()
	{
		_shadowsCollectionChanged.Disposable = null;
		_shadowPropertiesChanged.Disposable = null;
		_cornerRadiusChanged.Disposable = null;
	}

	protected override void OnApplyTemplate()
	{
		_canvas = GetTemplateChild(nameof(PART_Canvas)) as Canvas;


#if false // ANDROID: We keep that as a reference cause it would be better to use the hardware-accelerated version
        var skiaCanvas = new SKSwapChainPanel();
        skiaCanvas.PaintSurface += OnSurfacePainted;
#else
		var skiaCanvas = new SKXamlCanvas();
		skiaCanvas.PaintSurface += OnSurfacePainted;
#endif

#if __IOS__ || __MACCATALYST__
        skiaCanvas.Opaque = false;
#endif

		_shadowHost = skiaCanvas;
		_canvas?.Children.Insert(0, _shadowHost!);

		base.OnApplyTemplate();
	}

	/// <inheritdoc/>
	protected override void OnContentChanged(object oldContent, object newContent)
	{
		_cornerRadiusChanged.Disposable = null;

		if (oldContent is FrameworkElement oldElement)
		{
			_canvas?.Children.Remove(oldElement);
			oldElement.SizeChanged -= OnContentSizeChanged;
		}

		if (newContent is FrameworkElement newElement)
		{
			_currentContent = newElement;
			_currentContent.SizeChanged += OnContentSizeChanged;

			if (TryGetCornerRadius(newElement, out var cornerRadius))
			{
				var cornerRadiusProperty = newElement switch
				{
					Grid _ => Grid.CornerRadiusProperty,
					StackPanel _ => StackPanel.CornerRadiusProperty,
					ContentPresenter _ => ContentPresenter.CornerRadiusProperty,
					Border _ => Border.CornerRadiusProperty,
					Control _ => Control.CornerRadiusProperty,
					RelativePanel _ => RelativePanel.CornerRadiusProperty, 
					_ => default,

				};

				if (cornerRadiusProperty != null)
				{
					_cornerRadiusChanged.Disposable = newElement.RegisterDisposablePropertyChangedCallback(
						cornerRadiusProperty,
						(s, dp) => OnCornerRadiusChanged(s, dp)
					);
				}
			}

			_cornerRadius = cornerRadius;
		}

		_shadowHost?.Invalidate();
		base.OnContentChanged(oldContent, newContent);
	}

	private void OnCornerRadiusChanged(DependencyObject sender, DependencyProperty dp)
	{
		if (_currentContent is { })
		{
			if (TryGetCornerRadius(_currentContent, out var cornerRadius))
			{
				_cornerRadius = cornerRadius;
				_shadowHost?.Invalidate();
			}
		}
	}

	private static bool TryGetCornerRadius(FrameworkElement element, out CornerRadius cornerRadius)
	{
		CornerRadius? localCornerRadius = element switch
		{
			Control control => control.CornerRadius,
			StackPanel stackPanel => stackPanel.CornerRadius,
			RelativePanel relativePanel => relativePanel.CornerRadius,
			Grid grid => grid.CornerRadius,
			Border border => border.CornerRadius,
			_ => VisualTreeHelperEx.TryGetDpValue<CornerRadius>(element, "CornerRadius", out var value) ? value : default(CornerRadius?),
		};

		cornerRadius = localCornerRadius ?? new CornerRadius(0);
		return localCornerRadius != null;
	}

	private void OnContentSizeChanged(object sender, SizeChangedEventArgs args)
	{
		if (args.NewSize.Width > 0 && args.NewSize.Height > 0)
		{
			UpdateCanvasSize(args.NewSize.Width, args.NewSize.Height, Shadows);
			_shadowHost?.Invalidate();
		}
	}

	private void UpdateCanvasSize(double childWidth, double childHeight, ShadowCollection? shadows)
	{
		if (_currentContent == null || _canvas == null || _shadowHost == null)
		{
			return;
		}

		double absoluteMaxOffsetX = 0;
		double absoluteMaxOffsetY = 0;
		double maxBlurRadius = 0;
		double maxSpread = 0;

		if (shadows?.Any() == true)
		{
			absoluteMaxOffsetX = shadows.Max(s => Math.Abs(s.OffsetX));
			absoluteMaxOffsetY = shadows.Max(s => Math.Abs(s.OffsetY));
			maxBlurRadius = shadows.Max(s => s.BlurRadius);
			maxSpread = shadows.Max(s => s.Spread);
		}

		_canvas.Height = childHeight;
		_canvas.Width = childWidth;
#if __ANDROID__ || __IOS__
		_canvas.GetDispatcherCompat().Schedule(() => _canvas.InvalidateMeasure());
#endif
		double newHostHeight = childHeight + maxBlurRadius * 2 + absoluteMaxOffsetY * 2 + maxSpread * 2;
		double newHostWidth = childWidth + maxBlurRadius * 2 + absoluteMaxOffsetX * 2 + maxSpread * 2;
		_shadowHost.Height = newHostHeight;
		_shadowHost.Width = newHostWidth;

		double diffWidthShadowHostChild = newHostWidth - childWidth;
		double diffHeightShadowHostChild = newHostHeight - childHeight;

		float left = (float)(-diffWidthShadowHostChild / 2 + _currentContent.Margin.Left);
		float top = (float)(-diffHeightShadowHostChild / 2 + _currentContent.Margin.Top);

		Canvas.SetLeft(_shadowHost, left);
		Canvas.SetTop(_shadowHost, top);
	}
}
