import React, { useState, useEffect } from 'react';
import Navbar from '../components/Navbar';
import { useNavigate } from 'react-router-dom';
import '../components/Home.css'; // Updated path: from pages/ to components/

const Home = () => {
  const navigate = useNavigate();
  const [userRole, setUserRole] = useState('guest');

  useEffect(() => {
    const storedUser = JSON.parse(localStorage.getItem('user') || 'null');
    const roles = JSON.parse(localStorage.getItem('roles') || '[]');
    if (storedUser || roles.length > 0) {
      setUserRole(roles.includes('admin') ? 'admin' : 'user');
    }
    const token = localStorage.getItem('token');
    if (!token) {
      navigate('/');
    }
  }, [navigate]);

  const menuItems = {
    admin: ['Manage Users', 'Event Controls', 'Song Requests'],
    user: ['Browse Songs', 'Request a Song', 'Upcoming Events'],
    guest: ['View Songs', 'Login/Register']
  };

  return (
    <div className="home-container">
      <Navbar />
      <header className="home-header">
        <h1>Welcome to Blue Nest Karaoke</h1>
        <p>Bringing music to life, one song at a time.</p>
      </header>

      <nav className="menu">
        {menuItems[userRole]?.map((item, index) => (
          <button key={index} className="menu-item">{item}</button>
        ))}
      </nav>
    </div>
  );
};

export default Home;