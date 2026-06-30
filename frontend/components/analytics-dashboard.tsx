"use client"

import { useState, useMemo } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { BarChart, Bar, PieChart, Pie, Cell, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, LineChart, Line } from "recharts"
import { TrendingUp, CheckCircle, AlertCircle, Eye } from "lucide-react"

interface SavedAnalysis {
  id: string
  content: string
  type: "url" | "text"
  result: {
    verdict: string
    score: number
  }
  timestamp: number
  isFavorite: boolean
}

interface AnalyticsDashboardProps {
  analyses: SavedAnalysis[]
}

export default function AnalyticsDashboard({ analyses }: AnalyticsDashboardProps) {
  const stats = useMemo(() => {
    if (analyses.length === 0) {
      return {
        total: 0,
        fake: 0,
        legit: 0,
        uncertain: 0,
        fakePercent: 0,
        legitPercent: 0,
        uncertainPercent: 0,
        avgScore: 0,
        topDomains: [],
        verdictCounts: {},
        timelineData: [],
      }
    }

    const verdictCounts: Record<string, number> = {}
    let totalScore = 0
    let fakeCount = 0
    let legitCount = 0
    let uncertainCount = 0
    const domainCounts: Record<string, number> = {}
    const timelineData: Record<string, number> = {}

    analyses.forEach((analysis) => {
      // Count verdicts
      const verdict = analysis.result.verdict.toLowerCase()
      verdictCounts[verdict] = (verdictCounts[verdict] || 0) + 1

      if (verdict.includes("fake")) fakeCount++
      else if (verdict.includes("true")) legitCount++
      else uncertainCount++

      totalScore += analysis.result.score

      // Extract domain from content (if URL)
      if (analysis.type === "url") {
        try {
          const url = new URL(analysis.content)
          const domain = url.hostname
          domainCounts[domain] = (domainCounts[domain] || 0) + 1
        } catch (e) {
          // Invalid URL, skip
        }
      }

      // Timeline data (by date)
      const date = new Date(analysis.timestamp).toLocaleDateString()
      timelineData[date] = (timelineData[date] || 0) + 1
    })

    const total = analyses.length
    const topDomains = Object.entries(domainCounts)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5)
      .map(([domain, count]) => ({ domain, count }))

    const timelineArray = Object.entries(timelineData)
      .sort(([a], [b]) => new Date(a).getTime() - new Date(b).getTime())
      .map(([date, count]) => ({ date, analyses: count }))

    return {
      total,
      fake: fakeCount,
      legit: legitCount,
      uncertain: uncertainCount,
      fakePercent: ((fakeCount / total) * 100).toFixed(1),
      legitPercent: ((legitCount / total) * 100).toFixed(1),
      uncertainPercent: ((uncertainCount / total) * 100).toFixed(1),
      avgScore: (totalScore / total).toFixed(1),
      topDomains,
      verdictCounts,
      timelineData: timelineArray,
    }
  }, [analyses])

  const verdictData = [
    { name: "Likely Fake", value: stats.fake, fill: "#ef4444" },
    { name: "Likely True", value: stats.legit, fill: "#22c55e" },
    { name: "Uncertain", value: stats.uncertain, fill: "#eab308" },
  ].filter((item) => item.value > 0)

  return (
    <div className="w-full space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold bg-gradient-to-r from-[#abc7ff] to-[#00fbfb] bg-clip-text text-transparent">
            Analytics Dashboard
          </h2>
          <p className="text-gray-400 mt-1">Overview of your news analysis activity</p>
        </div>
        <TrendingUp className="w-8 h-8 text-[#abc7ff]" />
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <Card className="border border-white/10 hover:border-[#abc7ff]/50 transition">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-gray-400">Total Analyses</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{stats.total}</div>
            <p className="text-xs text-gray-500 mt-1">All reviewed items</p>
          </CardContent>
        </Card>

        <Card className="border border-red-500/30 hover:border-red-500/60 transition bg-red-500/10">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-red-400 flex items-center gap-2">
              <AlertCircle className="w-4 h-4" />
              Likely Fake
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold text-red-400">{stats.fake}</div>
            <p className="text-xs text-red-400/80 mt-1">{stats.fakePercent}% of total</p>
          </CardContent>
        </Card>

        <Card className="border border-green-500/30 hover:border-green-500/60 transition bg-green-500/10">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-green-400 flex items-center gap-2">
              <CheckCircle className="w-4 h-4" />
              Likely True
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold text-green-400">{stats.legit}</div>
            <p className="text-xs text-green-400/80 mt-1">{stats.legitPercent}% of total</p>
          </CardContent>
        </Card>

        <Card className="border border-yellow-500/30 hover:border-yellow-500/60 transition bg-yellow-500/10">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-yellow-400">Average Score</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold text-yellow-400">{stats.avgScore}</div>
            <p className="text-xs text-yellow-400/80 mt-1">out of 100</p>
          </CardContent>
        </Card>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Verdict Distribution Pie Chart */}
        {verdictData.length > 0 && (
          <Card className="border border-white/10">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Eye className="w-5 h-5" />
                Verdict Distribution
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={300}>
                <PieChart>
                  <Pie
                    data={verdictData}
                    cx="50%"
                    cy="50%"
                    labelLine={false}
                    label={({ name, value, percent }) =>
                      `${name}: ${value} (${(percent * 100).toFixed(0)}%)`
                    }
                    outerRadius={80}
                    fill="#8884d8"
                    dataKey="value"
                  >
                    {verdictData.map((entry, index) => (
                      <Cell key={`cell-${index}`} fill={entry.fill} />
                    ))}
                  </Pie>
                  <Tooltip />
                </PieChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        )}

        {/* Timeline Chart */}
        {stats.timelineData.length > 0 && (
          <Card className="border border-white/10">
            <CardHeader>
              <CardTitle>Analysis Timeline</CardTitle>
            </CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={stats.timelineData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#ffffff15" />
                  <XAxis
                    dataKey="date"
                    tick={{ fontSize: 12, fill: "#9aa4b2" }}
                    angle={-45}
                    textAnchor="end"
                    height={80}
                  />
                  <YAxis tick={{ fontSize: 12, fill: "#9aa4b2" }} />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: "#0b1221",
                      border: "1px solid #1f2937",
                      borderRadius: "8px",
                      color: "#e2e2e2",
                    }}
                  />
                  <Line
                    type="monotone"
                    dataKey="analyses"
                    stroke="#3b82f6"
                    strokeWidth={2}
                    dot={{ fill: "#3b82f6", r: 4 }}
                    activeDot={{ r: 6 }}
                  />
                </LineChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        )}
      </div>

      {/* Top Domains */}
      {stats.topDomains.length > 0 && (
        <Card className="border border-white/10">
          <CardHeader>
            <CardTitle>Most Analyzed Domains</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {stats.topDomains.map((domain, index) => (
                <div
                  key={domain.domain}
                  className="flex items-center justify-between p-3 bg-white/5 rounded-lg hover:bg-white/10 transition"
                >
                  <div className="flex items-center gap-3">
                    <Badge variant="secondary" className="w-8 h-8 flex items-center justify-center rounded-full">
                      {index + 1}
                    </Badge>
                    <span className="font-medium text-gray-200">{domain.domain}</span>
                  </div>
                  <Badge className="bg-[#abc7ff]/15 text-[#abc7ff]">{domain.count} analyses</Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Empty State */}
      {analyses.length === 0 && (
        <Card className="border border-dashed border-white/20 bg-white/5">
          <CardContent className="py-12">
            <div className="text-center">
              <Eye className="w-12 h-12 text-gray-500 mx-auto mb-4" />
              <p className="text-gray-300 font-medium">No analyses yet</p>
              <p className="text-gray-500 text-sm mt-1">Start analyzing articles to see statistics here</p>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
