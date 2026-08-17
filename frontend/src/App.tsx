import { Button, Layout, Menu, Space, Typography } from "antd";
import {
  CheckSquareOutlined,
  DashboardOutlined,
  LogoutOutlined,
  TeamOutlined,
} from "@ant-design/icons";
import { Link, Navigate, Route, Routes, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import DashboardPage from "./pages/DashboardPage";
import TasksPage from "./pages/TasksPage";
import CustomersPage from "./pages/CustomersPage";
import LoginPage from "./pages/LoginPage";

const { Header, Content, Sider } = Layout;

function RequireAuth({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();
  if (!user) return <Navigate to="/login" state={{ from: location }} replace />;
  return <>{children}</>;
}

function AppLayout() {
  const location = useLocation();
  const { user, logout } = useAuth();

  return (
    <Layout style={{ minHeight: "100vh" }}>
      <Header style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
        <div style={{ color: "#fff", fontSize: 18, fontWeight: "bold" }}>
          مدیریت وظایف و مشتریان
        </div>
        <Space>
          <Typography.Text style={{ color: "#fff" }}>{user?.fullName}</Typography.Text>
          <Button icon={<LogoutOutlined />} onClick={logout}>
            خروج
          </Button>
        </Space>
      </Header>
      <Layout>
        <Sider width={220} theme="light">
          <Menu
            mode="inline"
            selectedKeys={[location.pathname]}
            style={{ height: "100%" }}
            items={[
              {
                key: "/",
                icon: <DashboardOutlined />,
                label: <Link to="/">داشبورد</Link>,
              },
              {
                key: "/tasks",
                icon: <CheckSquareOutlined />,
                label: <Link to="/tasks">وظایف</Link>,
              },
              {
                key: "/customers",
                icon: <TeamOutlined />,
                label: <Link to="/customers">مشتریان</Link>,
              },
            ]}
          />
        </Sider>
        <Layout style={{ padding: 24 }}>
          <Content style={{ background: "#fff", padding: 24, borderRadius: 8 }}>
            <Routes>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/tasks" element={<TasksPage />} />
              <Route path="/customers" element={<CustomersPage />} />
            </Routes>
          </Content>
        </Layout>
      </Layout>
    </Layout>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          path="/*"
          element={
            <RequireAuth>
              <AppLayout />
            </RequireAuth>
          }
        />
      </Routes>
    </AuthProvider>
  );
}
