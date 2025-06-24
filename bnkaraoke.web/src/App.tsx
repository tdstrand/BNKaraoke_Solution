import React, { useEffect, useState, ReactNode, ErrorInfo } from 'react';
import { BrowserRouter as Router, Routes, Route, useLocation, useNavigate, Navigate } from "react-router-dom";
import Login from "./pages/Login";
import Home from "./pages/Home";
import Dashboard from "./pages/Dashboard";
import SpotifySearchTest from "./pages/SpotifySearchTest";
import PendingRequests from "./pages/PendingRequests";
import RequestSongPage from "./pages/RequestSongPage";
import SongManagerPage from "./pages/SongManagerPage";
import UserManagementPage from "./pages/UserManagementPage";
import Header from "./components/Header";
import ExploreSongs from "./pages/ExploreSongs";
import RegisterPage from "./pages/RegisterPage";
import KaraokeChannelsPage from "./pages/KaraokeChannelsPage";
import ChangePassword from "./pages/ChangePassword";
import Profile from "./pages/Profile";
import { EventContextProvider } from "./context/EventContext";

const routerFutureConfig = {
  v7_startTransition: true,
  v7_relativeSplatPath: true
};

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
    console.error('ErrorBoundary caught:', error, info);
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

  // Compute authentication state immediately
  const token = localStorage.getItem("token");
  const authenticated = !!token;
  const isLoginPage = ["/", "/register", "/change-password"].includes(location.pathname);
  console.log('HeaderWrapper initializing', { location: location.pathname, isAuthenticated, isLoginPage, mustChangePassword });

  useEffect(() => {
    console.log('HeaderWrapper useEffect running', { location: location.pathname, token });
    try {
      const storedMustChangePassword = localStorage.getItem("mustChangePassword");
      console.log('HeaderWrapper useEffect: token=', token, 'mustChangePassword=', storedMustChangePassword);

      setIsAuthenticated(authenticated);
      setMustChangePassword(storedMustChangePassword === "true");

      // Handle redirects
      if (authenticated) {
        if (storedMustChangePassword === "true" && location.pathname !== "/change-password") {
          console.log('HeaderWrapper redirecting to /change-password');
          navigate("/change-password", { replace: true });
        } else if ((location.pathname === "/" || location.pathname === "/register") && storedMustChangePassword !== "true") {
          console.log('HeaderWrapper redirecting to /dashboard');
          navigate("/dashboard", { replace: true });
        }
      } else if (!isLoginPage) {
        console.log('HeaderWrapper redirecting to /');
        navigate("/", { replace: true });
      }
    } catch (error) {
      console.error('HeaderWrapper useEffect error:', error, { message: "An error occurred while handling redirects", details: error });
    }
  }, [location.pathname, navigate, token, isLoginPage, authenticated]);

  const showHeader = !isLoginPage && authenticated;

  try {
    return (
      <>
        {showHeader && <Header />}
        {children}
      </>
    );
  } catch (error) {
    console.error('HeaderWrapper render error:', error);
    return <div>Error in HeaderWrapper: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

const App = () => {
  console.log('App component initializing');
  const [consoleErrors, setConsoleErrors] = useState<string[]>([]);
  const isDevelopment = process.env.NODE_ENV !== 'production';

  useEffect(() => {
    if (isDevelopment) {
      console.log('App useEffect running');
      console.log('Clearing localStorage on initial load in development mode');
      localStorage.clear();
      const originalConsoleError = console.error;
      console.error = (...args) => {
        const errorMessage = args.join(' ');
        setConsoleErrors((prev) => [...prev, `${new Date().toISOString()}: ${errorMessage}`]);
        originalConsoleError(...args);
      };
      window.onerror = (message, source, lineno) => {
        setConsoleErrors((prev) => [...prev, `${new Date().toISOString()}: Error: ${message} at ${source}:${lineno}`]);
        return true;
      };
      // Intercept check-in requests for debugging
      const originalFetch = window.fetch;
      window.fetch = async (url, options) => {
        if (typeof url === 'string' && url.includes('/api/events/') && url.includes('/attendance/check-in')) {
          console.log('Intercepted check-in request:', { url, options });
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

  return (
    <div>
      {isDevelopment && consoleErrors.length > 0 && !["/", "/register", "/change-password"].includes(window.location.pathname) && (
        <div style={{ color: 'red', margin: '10px', background: 'rgba(255, 255, 255, 0.1)', padding: '10px', borderRadius: '5px' }}>
          <h3>Console Errors:</h3>
          <ul>
            {consoleErrors.map((err, index) => (
              <li key={index}>{err}</li>
            ))}
          </ul>
        </div>
      )}
      <ErrorBoundary>
        <Router future={routerFutureConfig}>
          <EventContextProvider>
            <Routes>
              <Route path="/" element={<HeaderWrapper><Login /></HeaderWrapper>} />
              <Route path="/register" element={<HeaderWrapper><RegisterPage /></HeaderWrapper>} />
              <Route path="/change-password" element={<HeaderWrapper><ChangePassword /></HeaderWrapper>} />
              <Route path="/profile" element={<HeaderWrapper><Profile /></HeaderWrapper>} />
              <Route path="/home" element={<HeaderWrapper><Home /></HeaderWrapper>} />
              <Route path="/dashboard" element={<HeaderWrapper><Dashboard /></HeaderWrapper>} />
              <Route path="/request-song" element={<HeaderWrapper><RequestSongPage /></HeaderWrapper>} />
              <Route path="/spotify-search" element={<HeaderWrapper><SpotifySearchTest /></HeaderWrapper>} />
              <Route path="/pending-requests" element={<HeaderWrapper><PendingRequests /></HeaderWrapper>} />
              <Route path="/song-manager" element={<HeaderWrapper><SongManagerPage /></HeaderWrapper>} />
              <Route path="/user-management" element={<HeaderWrapper><UserManagementPage /></HeaderWrapper>} />
              <Route path="/explore-songs" element={<HeaderWrapper><ExploreSongs /></HeaderWrapper>} />
              <Route path="/karaoke-channels" element={<HeaderWrapper><KaraokeChannelsPage /></HeaderWrapper>} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </EventContextProvider>
        </Router>
      </ErrorBoundary>
    </div>
  );
};

export default App;