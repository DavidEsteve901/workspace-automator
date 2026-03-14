import React from 'react';

export class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, error: null, errorInfo: null };
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error };
  }

  componentDidCatch(error, errorInfo) {
    console.error("ErrorBoundary caught an error:", error, errorInfo);
    this.setState({ errorInfo });
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ padding: '20px', background: 'rgba(20,20,20,0.95)', color: 'white', height: '100%', width: '100%', overflow: 'auto', display: 'flex', flexDirection: 'column', boxSizing: 'border-box', fontFamily: 'system-ui' }}>
          <h2 style={{ color: '#ff4444' }}>Error crítico en el Editor de Zonas</h2>
          <p>La interfaz ha fallado debido a un error inesperado. Por favor, reporta este error.</p>
          <details style={{ whiteSpace: 'pre-wrap', marginTop: '10px', background: '#000', padding: '10px', borderRadius: '5px' }}>
            <summary style={{ cursor: 'pointer', marginBottom: '10px' }}>Ver detalles técnicos</summary>
            <div style={{ color: '#ff8888', fontWeight: 'bold' }}>{this.state.error && this.state.error.toString()}</div>
            <div style={{ fontSize: '12px', color: '#ccc', marginTop: '10px' }}>{this.state.errorInfo && this.state.errorInfo.componentStack}</div>
          </details>
        </div>
      );
    }
    return this.props.children;
  }
}
