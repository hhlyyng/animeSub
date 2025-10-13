import { useState, useEffect } from 'react';

const DebugSize = () => {
  const [sizes, setSizes] = useState({
    window: { width: 0, height: 0 },
    viewport: { width: 0, height: 0 },
    html: { width: 0, height: 0 },
    body: { width: 0, height: 0 },
    root: { width: 0, height: 0 }
  });

  useEffect(() => {
    const updateSizes = () => {
      const root = document.getElementById('root');
      setSizes({
        window: {
          width: window.innerWidth,
          height: window.innerHeight
        },
        viewport: {
          width: document.documentElement.clientWidth,
          height: document.documentElement.clientHeight
        },
        html: {
          width: document.documentElement.offsetWidth,
          height: document.documentElement.offsetHeight
        },
        body: {
          width: document.body.offsetWidth,
          height: document.body.offsetHeight
        },
        root: {
          width: root?.offsetWidth || 0,
          height: root?.offsetHeight || 0
        }
      });
    };

    updateSizes();
    window.addEventListener('resize', updateSizes);
    
    return () => window.removeEventListener('resize', updateSizes);
  }, []);

  return (
    <div style={{
      padding: '20px',
      fontFamily: 'monospace',
      fontSize: '16px',
      backgroundColor: '#f0f0f0',
      minHeight: '100vh'
    }}>
      <h1 style={{ marginBottom: '20px' }}>尺寸调试信息</h1>
      
      <div style={{ backgroundColor: 'white', padding: '15px', marginBottom: '10px', borderRadius: '5px' }}>
        <strong>Window (视口实际大小):</strong>
        <div>宽度: {sizes.window.width}px</div>
        <div>高度: {sizes.window.height}px</div>
      </div>

      <div style={{ backgroundColor: 'white', padding: '15px', marginBottom: '10px', borderRadius: '5px' }}>
        <strong>Viewport (document.documentElement.client):</strong>
        <div>宽度: {sizes.viewport.width}px</div>
        <div>高度: {sizes.viewport.height}px</div>
      </div>

      <div style={{ backgroundColor: 'white', padding: '15px', marginBottom: '10px', borderRadius: '5px' }}>
        <strong>HTML:</strong>
        <div>宽度: {sizes.html.width}px</div>
        <div>高度: {sizes.html.height}px</div>
      </div>

      <div style={{ backgroundColor: 'white', padding: '15px', marginBottom: '10px', borderRadius: '5px' }}>
        <strong>Body:</strong>
        <div>宽度: {sizes.body.width}px</div>
        <div>高度: {sizes.body.height}px</div>
      </div>

      <div style={{ backgroundColor: 'white', padding: '15px', marginBottom: '10px', borderRadius: '5px' }}>
        <strong>#root:</strong>
        <div>宽度: {sizes.root.width}px</div>
        <div>高度: {sizes.root.height}px</div>
      </div>

      <div style={{ 
        backgroundColor: sizes.root.width > sizes.window.width ? '#ffcccc' : '#ccffcc', 
        padding: '15px', 
        borderRadius: '5px',
        fontWeight: 'bold'
      }}>
        {sizes.root.width > sizes.window.width 
          ? `⚠️ 问题：#root (${sizes.root.width}px) 超出了视口 (${sizes.window.width}px)!`
          : `✅ 正常：#root 宽度在视口范围内`
        }
      </div>
    </div>
  );
};

export default DebugSize;