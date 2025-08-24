// src/App.tsx
import React, { useEffect, useState, ReactNode, ErrorInfo } from 'react';
import { BrowserRouter, Routes, Route, useLocation, useNavigate, Navigate } from 'react-router-dom';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import SpotifySearchTest from './pages/SpotifySearchTest';
import PendingRequests from './pages/PendingRequests';
import RequestSongPage from './pages/RequestSongPage';
import SongManagerPage from './pages/SongManagerPage';
import UserManagementPage from './pages/UserManagementPage';
import EventManagementPage from './pages/EventManagement';
import Header from './components/Header';
import ExploreSongs from './pages/ExploreSongs';
import RegisterPage from './pages/RegisterPage';
import KaraokeChannelsPage from './pages/KaraokeChannelsPage';
import ChangePassword from './pages/ChangePassword';
import Profile from './pages/Profile';
import AddRequests from './pages/AddRequests';
import { EventContextProvider } from './context/EventContext';
import './App.css';
import { logoutAndRedirect } from './utils/auth';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: string | null;
  errorInfo: ErrorInfo | null;
}

class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null, errorInfo: null };

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    return { error: error.message };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('[ERROR_BOUNDARY] Caught:', error, info);
    this.setState({ error: error.message, errorInfo: info });
  }

  render() {
    if (this.state.error) {
      return (
        <div style={{ color: 'red', margin: '10px', padding: '10px', background: 'rgba(255, 255, 255, 0.1)', borderRadius: '5px' }}>
          <h3>Application Error</h3>
          <p>{this.state.error}</p>
          <pre>{this.state.errorInfo?.componentStack}</pre>
        </div>
      );
    }
    return this.props.children;
  }
}

const HeaderWrapper: React.FC<{ children: ReactNode }> = ({ children }) => {
  const location = useLocation();
  const navigate = useNavigate();
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [mustChangePassword, setMustChangePassword] = useState<boolean | null>(null);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.warn("[HEADER_WRAPPER] No token or userName found");
      logoutAndRedirect(navigate);
      return false;
    }

    try {
      if (token.split('.').length !== 3) {
        console.warn("[HEADER_WRAPPER] Malformed token: does not contain three parts");
        logoutAndRedirect(navigate);
        return false;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.warn("[HEADER_WRAPPER] Token expired:", new Date(exp).toISOString());
        logoutAndRedirect(navigate);
        return false;
      }
      console.log("[HEADER_WRAPPER] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return true;
    } catch (err) {
      console.error("[HEADER_WRAPPER] Token validation error:", err);
      logoutAndRedirect(navigate);
      return false;
    }
  };

  const isLoginPage = ["/", "/register", "/change-password"].includes(location.pathname);
  console.log('[HEADER_WRAPPER] Initializing', { location: location.pathname, isAuthenticated, isLoginPage, mustChangePassword });

  useEffect(() => {
    console.log('[HEADER_WRAPPER] useEffect running', { location: location.pathname, token: localStorage.getItem("token") });
    try {
      const storedMustChangePassword = localStorage.getItem("mustChangePassword");
      console.log('[HEADER_WRAPPER] useEffect: token=', localStorage.getItem("token"), 'mustChangePassword=', storedMustChangePassword);

      if (isLoginPage) {
        setIsAuthenticated(false);
        return;
      }

      const isValidToken = validateToken();
      setIsAuthenticated(isValidToken);

      if (!isValidToken && !isLoginPage) {
        return; // validateToken already handled redirect
      }

      setMustChangePassword(storedMustChangePassword === "true");

      if (storedMustChangePassword === "true" && location.pathname !== "/change-password") {
        console.log('[HEADER_WRAPPER] Redirecting to /change-password');
        navigate("/change-password", { replace: true });
      } else if ((location.pathname === "/" || location.pathname === "/register") && storedMustChangePassword !== "true") {
        console.log('[HEADER_WRAPPER] Redirecting to /dashboard');
        navigate("/dashboard", { replace: true });
      }
    } catch (error) {
      console.error('[HEADER_WRAPPER] useEffect error:', error);
    }
  }, [location.pathname, navigate, isLoginPage]);

  const showHeader = !isLoginPage && isAuthenticated;

  try {
    return (
      <>
        {showHeader && <Header />}
        {children}
      </>
    );
  } catch (error) {
    console.error('[HEADER_WRAPPER] Render error:', error);
    return <div>Error in HeaderWrapper: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

const App: React.FC = () => {
  console.log('[APP] Component initializing');
  const [consoleErrors, setConsoleErrors] = useState<string[]>([]);
  const isDevelopment = process.env.NODE_ENV !== 'production';

  useEffect(() => {
    if (isDevelopment) {
      console.log('[APP] useEffect running');
      const originalConsoleError = console.error;
      console.error = (...args) => {
        const errorMessage = args.join(' ');
        // Ignore noisy translation errors that appear on mobile Brave
        if (errorMessage && errorMessage.includes('translateDisabled')) {
          return;
        }
        setConsoleErrors((prev) => [...prev, `${new Date().toISOString()}: ${errorMessage}`]);
        originalConsoleError(...args);
      };
      window.onerror = (message, source, lineno) => {
        const msg = String(message);
        // Skip logging of translation-related errors that Brave injects
        if (msg.includes('translateDisabled')) {
          return true;
        }
        setConsoleErrors((prev) => [...prev, `${new Date().toISOString()}: Error: ${msg} at ${source}:${lineno}`]);
        return true;
      };
      const originalFetch = window.fetch;
      window.fetch = async (url, options) => {
        if (typeof url === 'string' && url.includes('/api/events/') && url.includes('/attendance/check-in')) {
          console.log('[APP] Intercepted check-in request:', { url, options });
        }
        return originalFetch(url, options);
      };
      return () => {
        console.error = originalConsoleError;
        window.onerror = null;
        window.fetch = originalFetch;
      };
    }
  }, [isDevelopment]);

  try {
    return (
      <div className="app-container mobile-app">
        {isDevelopment && consoleErrors.length > 0 && !["/", "/register", "/change-password"].includes(window.location.pathname) && (
          <div className="console-errors mobile-console-errors" style={{ color: '#f97316', margin: '10px', background: 'rgba(255, 255, 255, 0.1)', padding: '10px', borderRadius: '5px' }}>
            <h3>Console Errors:</h3>
            <ul>
              {consoleErrors.map((err, index) => (
                <li key={index}>{err}</li>
              ))}
            </ul>
          </div>
        )}
        <ErrorBoundary>
          <BrowserRouter>
            <EventContextProvider>
              <Routes>
                <Route path="/" element={<HeaderWrapper><Login /></HeaderWrapper>} />
                <Route path="/register" element={<HeaderWrapper><RegisterPage /></HeaderWrapper>} />
                <Route path="/change-password" element={<HeaderWrapper><ChangePassword /></HeaderWrapper>} />
                <Route path="/profile" element={<HeaderWrapper><Profile /></HeaderWrapper>} />
                <Route path="/dashboard" element={<HeaderWrapper><Dashboard /></HeaderWrapper>} />
                <Route path="/request-song" element={<HeaderWrapper><RequestSongPage /></HeaderWrapper>} />
                <Route path="/spotify-search" element={<HeaderWrapper><SpotifySearchTest /></HeaderWrapper>} />
                <Route path="/pending-requests" element={<HeaderWrapper><PendingRequests /></HeaderWrapper>} />
                <Route path="/song-manager" element={<HeaderWrapper><SongManagerPage /></HeaderWrapper>} />
                <Route path="/user-management" element={<HeaderWrapper><UserManagementPage /></HeaderWrapper>} />
                <Route path="/event-management" element={<HeaderWrapper><EventManagementPage /></HeaderWrapper>} />
                <Route path="/explore-songs" element={<HeaderWrapper><ExploreSongs /></HeaderWrapper>} />
                <Route path="/karaoke-channels" element={<HeaderWrapper><KaraokeChannelsPage /></HeaderWrapper>} />
                <Route path="/admin/add-requests" element={<HeaderWrapper><AddRequests /></HeaderWrapper>} />
                <Route path="*" element={localStorage.getItem("token") ? <Navigate to="/dashboard" replace /> : <Navigate to="/" replace />} />
              </Routes>
            </EventContextProvider>
          </BrowserRouter>
        </ErrorBoundary>
      </div>
    );
  } catch (error) {
    console.error('[APP] Render error:', error);
    return <div>Error in App: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default App;