// src/pages/Home.jsx
import React from 'react';
import { useNavigate } from 'react-router-dom';
import './Home.css';

const Home = () => {
  const navigate = useNavigate();

  const handleLogin = () => {
    navigate('/login');
  };

  const handleRegister = () => {
    navigate('/register');
  };

  return (
    <div className="home-container mobile-home">
      <h1>Welcome to BNKaraoke</h1>
      <p>Explore songs, manage events, and join karaoke sessions!</p>
      <div className="home-actions">
        <button
          className="home-button login-button"
          onClick={handleLogin}
          onTouchStart={handleLogin}
        >
          Login
        </button>
        <button
          className="home-button register-button"
          onClick={handleRegister}
          onTouchStart={handleRegister}
        >
          Register
        </button>
      </div>
    </div>
  );
};

export default Home;