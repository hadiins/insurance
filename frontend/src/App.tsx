import { Layout, Menu } from "antd";
import { CheckSquareOutlined, DashboardOutlined, TeamOutlined } from "@ant-design/icons";
import { Link, Route, Routes, useLocation } from "react-router-dom";
import DashboardPage from "./pages/DashboardPage";
import TasksPage from "./pages/TasksPage";
import CustomersPage from "./pages/CustomersPage";

const { Header, Content, Sider } = Layout;

export default function App() {
  const location = useLocation();

  return (
    <Layout style={{ minHeight: "100vh" }}>
      <Header style={{ display: "flex", alignItems: "center" }}>
        <div style={{ color: "#fff", fontSize: 18, fontWeight: "bold" }}>
          مدیریت وظایف و مشتریان
        </div>
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
